# Dlaczego tak, a nie inaczej

Notatki projektowe. Każda sekcja tłumaczy jedną decyzję: co wybrano, jaka była alternatywa i czym za to płacimy.

## Dlaczego Modbus TCP

Bo to najczęstszy protokół, jakim rozmawiają sterowniki PLC, falowniki, mierniki i czujniki. Jest stary i prosty: klient pyta o rejestry, urządzenie odpowiada. Nie ma typów, nazw ani schematu — są tylko 16-bitowe rejestry pod adresami, a ich znaczenie opisuje dokumentacja urządzenia (u nas tabela „Register map” w README i klasa `RegisterMap`).

Alternatywą było OPC UA, które ma model informacji, typy i subskrypcje. Jest bogatsze, ale też dużo cięższe. Modbus pozwala pokazać rzeczy, z którymi naprawdę trzeba się zmierzyć przy integracji z maszynami: kolejność bajtów, skalowanie wartości, wartości 32-bitowe rozbite na dwa rejestry i urządzenia, które czasem nie odpowiadają.

## Dlaczego jeden serwer Modbus i cztery unit id

Symulator to jeden serwer TCP na porcie 5020, a każda maszyna to osobny *unit id* (1–4). Tak wygląda typowa bramka Modbus TCP ↔ RS-485: jeden adres IP, za nim kilka urządzeń na magistrali szeregowej.

Kolektor tego nie zakłada. Każda maszyna w konfiguracji ma własne `Host`, `Port` i `UnitId`, więc tak samo dobrze obsłuży cztery osobne urządzenia z własnymi adresami IP.

## Dlaczego temperatura jest w dziesiątych częściach stopnia

Rejestr ma 16 bitów i nie ma miejsca na liczbę zmiennoprzecinkową. Standardowy trik to skalowanie: 62,4 °C zapisuje się jako 624. Rejestr jest interpretowany jako liczba ze znakiem (`short`), żeby dało się zapisać też ujemne temperatury — jest na to osobny test.

## Dlaczego licznik sztuk zajmuje dwa rejestry

16 bitów kończy się na 65 535 sztukach, a to dla maszyny kilka dni pracy. Licznik jest 32-bitowy i zajmuje rejestry 3 i 4, **starsze słowo pierwsze**. Kolejność słów to klasyczna pułapka: Modbus definiuje kolejność bajtów w rejestrze (big-endian), ale nie kolejność rejestrów w liczbie 32-bitowej. Każdy producent robi to po swojemu, dlatego jest to zapisane w `RegisterMap` i sprawdzone testem `Part_count_spans_two_registers_high_word_first`.

## Dlaczego odpytywanie (polling), a nie powiadomienia

Modbus nie ma powiadomień. Urządzenie tylko odpowiada na pytania, nigdy samo nie zaczyna rozmowy. Kolektor musi więc pytać w kółko.

`ModbusPoller` to `BackgroundService` z `PeriodicTimer`. Timer, a nie `Task.Delay` w pętli, bo `Task.Delay` odlicza od końca poprzedniego odczytu, więc odstępy rosłyby o czas trwania odczytu. `PeriodicTimer` trzyma stały rytm, dzięki czemu próbki są równo rozłożone w czasie.

Wszystkie maszyny są odpytywane równolegle (`Task.WhenAll`), każda po swoim połączeniu TCP. Jedna wolna albo martwa maszyna nie opóźnia pozostałych dłużej niż o timeout.

## Dlaczego po błędzie połączenie jest zrywane

`MachineConnection` przy każdym wyjątku robi `Disconnect()`. Po timeoucie nie wiadomo, w jakim stanie jest gniazdo: odpowiedź mogła przyjść w połowie i przy następnym odczycie dostalibyśmy resztki starej ramki. Czyste połączenie przy następnym cyklu jest tańsze niż szukanie takiego błędu.

Timeout pilnuje `CancellationTokenSource.CancelAfter` na tokenie, który obejmuje i łączenie, i odczyt. Jedno miejsce zamiast polegania na tym, jak biblioteka traktuje `ReadTimeout` przy operacjach asynchronicznych.

## Dlaczego alarm to zmiana stanu, a nie stan

`AlarmTracker` zwraca tylko **zmiany**: alarm się zaczął albo się skończył. Gdyby zwracał „temperatura za wysoka” przy każdym odczycie, przy odpytywaniu co sekundę jedno zdarzenie dałoby 60 wpisów na minutę.

Tracker jest celowo czystą klasą bez bazy, SignalR i zegara systemowego — dostaje odczyt, zwraca listę zmian. Dzięki temu cała logika alarmów jest pokryta szybkimi testami jednostkowymi.

## Dlaczego histereza

Temperatura z czujnika szumi. Jeśli limit to 80 °C, a temperatura waha się między 79,8 a 80,2, alarm bez histerezy zapalałby się i gasł co sekundę. Operator przestałby na niego patrzeć.

Dlatego alarm włącza się powyżej limitu, a wyłącza dopiero poniżej `limit − histereza` (dla frezarek 80 i 77 °C). Pomiędzy nic się nie zmienia. W automatyce nazywa się to *deadband* i jest standardem w każdym systemie alarmowym.

## Dlaczego „brak komunikacji” dopiero po kilku nieudanych odczytach

Pojedynczy timeout w sieci przemysłowej to codzienność. Alarm po pierwszym nieudanym odczycie byłby fałszywym alarmem. Kolektor czeka na trzy kolejne porażki (`FailedPollsBeforeOffline`). Pierwszy udany odczyt zamyka alarm i zeruje licznik.

## Dlaczego alarmy są odtwarzane po restarcie

Tracker trzyma aktywne alarmy w pamięci. Po restarcie API zacząłby od zera i przy pierwszym gorącym odczycie otworzyłby drugi alarm tego samego rodzaju, choć pierwszy dalej jest otwarty w bazie. Dlatego `ModbusPoller` na starcie wczytuje otwarte alarmy i przekazuje je do `Restore()`.

Na wypadek błędu w kodzie jest też zabezpieczenie w bazie: unikalny indeks częściowy na `(MachineId, Kind)` z warunkiem `ClearedAt IS NULL`. Baza fizycznie nie pozwoli zapisać dwóch otwartych alarmów tego samego rodzaju dla jednej maszyny. Zamknięte alarmy indeks pomija, więc historia może mieć ich dowolnie dużo.

## Dlaczego potwierdzenie alarmu nie nadpisuje czasu

`POST /api/alarms/{id}/acknowledge` ustawia `AcknowledgedAt` tylko raz. Drugie kliknięcie (na przykład drugiego operatora na innym ekranie) nie zmienia czasu, bo liczy się moment, w którym ktoś **pierwszy** zareagował. Operacja jest przez to idempotentna i można ją bezpiecznie ponowić.

## Dlaczego jeden zapis do bazy na cykl

Cztery odczyty i ewentualne zmiany alarmów z jednego cyklu idą jednym `SaveChangesAsync`, czyli jedną transakcją. Zapis co sekundę na maszynę osobno to cztery razy więcej rozmów z bazą bez żadnej korzyści.

Wyjątek w cyklu (np. chwilowy brak bazy) jest łapany i logowany. Tracimy jedną próbkę, ale kolektor działa dalej. Domyślnie nieobsłużony wyjątek w `BackgroundService` zatrzymuje całą aplikację.

## Dlaczego odczyty są kasowane po 24 godzinach

Jedna maszyna co sekundę to 86 400 wierszy na dobę. Dashboard patrzy najwyżej godzinę wstecz. `ReadingRetention` co 10 minut kasuje starsze odczyty jednym `ExecuteDeleteAsync`, czyli jednym `DELETE ... WHERE` w SQL, bez wczytywania wierszy do pamięci. Alarmy zostają, bo jest ich mało, a są historią zdarzeń.

Przy dłuższej historii lepsze byłoby TimescaleDB z agregatami (np. średnia minutowa) albo partycjonowanie tabeli po czasie i usuwanie całych partycji.

## Dlaczego licznik sztuk jest w bazie typu `long`

PostgreSQL nie ma typów bez znaku. `uint` z Modbusa nie zmieści się w `integer` (max 2,1 mld), więc kolumna jest `bigint`. Tak samo kod błędu (`ushort`) ląduje w `integer`. Konwersja jest w jednym miejscu: `StoredReading.From`.

## Dlaczego czas jest przycinany do mikrosekund

To błąd, który wyłapał test integracyjny `Acknowledging_keeps_the_first_timestamp`. .NET trzyma czas z dokładnością do 100 ns, PostgreSQL do 1 µs. Pierwsze potwierdzenie zwracało czas z pamięci (7 cyfr po przecinku), a drugie ten sam czas odczytany z bazy (6 cyfr). Formalnie dwa różne czasy, więc test się nie zgadzał, choć w bazie wszystko było dobrze.

Rozwiązanie: `DatabaseClock.GetUtcNowForDatabase()` od razu przycina czas do mikrosekund. To, co API zwraca i rozsyła przez SignalR, jest wtedy identyczne z tym, co później przeczyta się z bazy.

## Dlaczego SignalR, a dashboard i tak pyta REST

Hub (`TelemetryHub`) tylko wysyła. Nie ma żadnych metod wywoływanych przez klienta. Dashboard robi dwie rzeczy w ustalonej kolejności:

1. łączy się z hubem i zaczyna słuchać,
2. dopiero potem pobiera przez REST stan maszyn, historię i alarmy.

Odwrotna kolejność zostawiałaby dziurę: zmiana, która przyszłaby między pobraniem stanu a podłączeniem się, by przepadła. Po zerwaniu połączenia (`Reconnected`) dashboard pobiera stan jeszcze raz, bo wszystko, co się działo w międzyczasie, jest tylko w bazie.

Hub jest typowany (`Hub<ITelemetryClient>`): nazwy komunikatów `MachineUpdated` i `AlarmChanged` to metody interfejsu, a nie stringi wpisywane ręcznie po obu stronach.

`WithAutomaticReconnect()` działa tylko dla połączenia, które raz już się udało. Jeśli API jest wyłączone w chwili otwarcia dashboardu, pierwsze połączenie trzeba ponawiać ręcznie — stąd pętla w `TelemetryFeed.ConnectAsync`.

## Dlaczego Blazor WebAssembly, a nie Blazor Server

Blazor Server trzyma stan każdej karty przeglądarki na serwerze i całe UI chodzi przez stałe połączenie. Przy dashboardzie, który ma wisieć na ekranie na hali cały dzień, oznacza to obciążenie serwera za każdy otwarty ekran.

WebAssembly po zbudowaniu to zwykłe pliki statyczne: w Dockerze serwuje je nginx, a API widzi tylko lekkie połączenie SignalR i kilka zapytań REST. Ten sam podział co Angular + REST w OrderStream, tylko po obu stronach jest C#. Rekordy `MachineStatus`, `ReadingPoint` i `AlarmInfo` z `LineWatch.Core.Contracts` są używane i przez API, i przez dashboard, więc zmiana kontraktu to błąd kompilacji, a nie błąd w przeglądarce.

Koszt: pierwsze ładowanie jest cięższe (środowisko .NET w WebAssembly), a adres API trzeba podać w `appsettings.json` dashboardu. W Dockerze nadpisuje go skrypt startowy nginx ze zmiennej `API_BASE_URL`, więc ten sam obraz działa z dowolnym API.

## Dlaczego własny wykres w SVG

Potrzebna jest jedna linia, linia limitu i dwie osie. Biblioteka wykresów to sporo kodu do pobrania przez przeglądarkę i własne API do nauczenia. `TrendChart.razor` robi dokładnie to, co trzeba.

Pierwsza wersja rysowała wszystko, łącznie z podpisami osi, w jednym SVG ze stałym `viewBox`. Na monitorze wyglądało dobrze, ale na telefonie cały rysunek skalował się w dół razem z tekstem: wykres robił się płaski, a podpisy miały po 4 piksele. Obecna wersja rozdziela te dwie rzeczy:

- **linia** jest w SVG z `preserveAspectRatio="none"`, czyli rozciąga się do dowolnego prostokąta, a `vector-effect: non-scaling-stroke` pilnuje, żeby grubość linii się przy tym nie zmieniała;
- **podpisy** to zwykły HTML ustawiony w procentach wysokości, więc tekst ma zawsze ten sam rozmiar, a wysokość wykresu ustala CSS (240 px na komputerze, 170 px na telefonie).

Do tego dwie rzeczy, które w bibliotece trzeba by konfigurować:

- **przerwa w linii**, gdy między punktami jest więcej niż 5 sekund — maszyna była offline i rysowanie prostej przez tę dziurę udawałoby dane, których nie ma;
- **okrągłe wartości na osi** (40, 60, 80 zamiast 41, 61, 81) — zakres jest zaokrąglany do „ładnego” kroku 1, 2 albo 5 × 10ⁿ.

Prawa krawędź wykresu to czas ostatniego punktu, a nie zegar przeglądarki. Jeśli zegar komputera operatora się spieszy albo spóźnia, wykres i tak się nie przesuwa.

## Dlaczego migracje przy starcie API

`Database.MigrateAsync()` w `Program.cs` jest wygodne: `docker compose up` i baza od razu ma schemat. Przy jednej instancji to w porządku. Przy kilku instancjach startujących równocześnie wszystkie próbowałyby migrować naraz — wtedy migracje odpala się osobnym krokiem w pipeline (np. `dotnet ef migrations bundle`), zanim wystartuje nowa wersja.

## Dlaczego testy integracyjne mają prawdziwy serwer Modbus

`LineWatchApiFactory` uruchamia prawdziwe API (`WebApplicationFactory`), prawdziwego PostgreSQL w Dockerze (Testcontainers) i serwer Modbus wewnątrz procesu testów. Test ustawia rejestry tak, jak zrobiłaby to maszyna, i sprawdza wynik przez REST albo SignalR. Przechodzi więc cała ścieżka: TCP, dekodowanie, alarmy, zapis, odpowiedź HTTP.

Druga maszyna w testach wskazuje na port, na którym nic nie słucha — w ten sposób test „brak komunikacji” nie musi niczego wyłączać.

Kolektor działa we własnym rytmie, więc testy nie śpią stałego czasu, tylko czekają na warunek (`Eventually`) najwyżej 10 sekund. Na szybkiej maszynie test kończy się po kilkuset milisekundach, na wolnym CI ma zapas.

## Czego tu świadomie nie ma

- **Logowania.** Każdy, kto otworzy dashboard, może potwierdzić alarm, a w bazie nie zapisuje się, kto to zrobił. W prawdziwym systemie to podstawa (kto i kiedy zareagował).
- **OPC UA.** Naturalny następny krok: drugi rodzaj połączenia obok Modbusa, za tym samym interfejsem odczytu.
- **Konfiguracji maszyn w bazie.** Lista maszyn jest w `appsettings.json`. Dla stałej linii wystarcza, ale dodanie maszyny wymaga restartu.
- **Skalowania.** Kolektor i hub zakładają jedną instancję API. Przy kilku trzeba by wybrać jedną instancję, która odpytuje maszyny, a SignalR podłączyć przez backplane (np. Redis), żeby komunikaty docierały do klientów podłączonych do innych instancji.
