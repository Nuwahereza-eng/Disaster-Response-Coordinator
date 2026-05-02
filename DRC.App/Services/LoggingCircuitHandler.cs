using Microsoft.AspNetCore.Components.Server.Circuits;

namespace DRC.App.Services
{
    /// <summary>
    /// Logs every unhandled exception inside a Blazor Server circuit so that
    /// "An unhandled error has occurred. Reload" in the browser is paired
    /// with a full stack trace in Render's runtime logs. Without this, the
    /// generic UI message hides the root cause.
    /// </summary>
    public class LoggingCircuitHandler : CircuitHandler
    {
        private readonly ILogger<LoggingCircuitHandler> _logger;

        public LoggingCircuitHandler(ILogger<LoggingCircuitHandler> logger)
        {
            _logger = logger;
        }

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Circuit opened: {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Circuit closed: {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }
    }
}
