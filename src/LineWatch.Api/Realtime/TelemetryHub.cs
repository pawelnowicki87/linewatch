using LineWatch.Core.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace LineWatch.Api.Realtime;

/// <summary>
/// Push-only hub: clients load the current state over REST, then listen here for changes.
/// </summary>
public sealed class TelemetryHub : Hub<ITelemetryClient>;
