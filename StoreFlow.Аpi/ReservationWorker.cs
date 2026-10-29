using StoreFlow.Application.Interfaces;
namespace StoreFlow.Аpi;

public sealed class ReservationWorker(IServiceScopeFactory scopes, ILogger<ReservationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IOrderService>().ExpireReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Не вдалося звільнити прострочені резерви."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
