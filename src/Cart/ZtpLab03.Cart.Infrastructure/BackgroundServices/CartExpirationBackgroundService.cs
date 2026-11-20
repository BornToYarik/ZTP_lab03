using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZtpLab03.Cart.Application.UseCases.ExpireInactiveCarts;
using ZtpLab03.SharedKernel.CQRS;

namespace ZtpLab03.Cart.Infrastructure.BackgroundServices;

/// <summary>
/// Hosted background service that periodically dispatches <see cref="ExpireInactiveCartsCommand"/>
/// to enforce the 15-minute product lock expiration and cart cleanup requirement (+0.5 grade criterion):
/// "Jako użytkownik, chcę, aby blokada produktów w koszyku wygasała po 15 minutach,
/// a koszyk był usuwany, aby zwolnić zarezerwowane przedmioty, gdy przestanę kontynuować zakupy."
/// </summary>
public sealed class CartExpirationBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CartExpirationBackgroundService> _logger;

    public CartExpirationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CartExpirationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                var expiredCount = await mediator.SendCommandAsync<ExpireInactiveCartsCommand, int>(
                    new ExpireInactiveCartsCommand(),
                    stoppingToken);

                if (expiredCount > 0)
                {
                    _logger.LogInformation(
                        "Expired {ExpiredCount} inactive shopping cart(s) and released their 15-minute product locks.",
                        expiredCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking for expired shopping carts.");
            }
        }
    }
}
