using System.Diagnostics;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.Model;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

namespace eShop.Basket.API.Grpc;

public class BasketService(
    IBasketRepository repository,
    ILogger<BasketService> logger) : Basket.BasketBase
{
    private static readonly ActivitySource activitySource = new("BasketService");
    private static readonly Meter meter = new("BasketService");

    private static readonly Counter<long> addToBasketCounter = meter.CreateCounter<long>("basket.add_to_basket.count");
    private static readonly Counter<long> addToBasketItemsAddedCounter = meter.CreateCounter<long>("basket.add_to_basket.items_added");
    private static readonly Counter<long> addToBasketErrorsCounter = meter.CreateCounter<long>("basket.add_to_basket.errors.count");

    [AllowAnonymous]
    public override async Task<CustomerBasketResponse> GetBasket(GetBasketRequest request, ServerCallContext context)
    {

        using var activity = activitySource.StartActivity("GetBasket", ActivityKind.Server);    
        
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "User not authenticated");
            activity?.AddEvent(new ActivityEvent("ERROR: User not authenticated"));
            return new();
        }

        activity.SetTag("user.id", userId);
        activity.SetTag("basket.access_time", DateTime.UtcNow);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin GetBasketById call from method {Method} for basket id {Id}", context.Method, userId);
        }

        var data = await repository.GetBasketAsync(userId);

        if (data is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Basket found with success");
            activity?.AddEvent(new ActivityEvent("SUCCESS: Basket found with success"));
            return MapToCustomerBasketResponse(data, activity);
        }

        activity?.SetStatus(ActivityStatusCode.Ok, "Basket Empty");

        return new();
    }

    public override async Task<CustomerBasketResponse> UpdateBasket(UpdateBasketRequest request, ServerCallContext context)
    {
        using var activity = activitySource.StartActivity("UpdateBasket", ActivityKind.Server);

        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "User not authenticated");
            activity?.AddEvent(new ActivityEvent("ERROR (UpdateBasket): User not authenticated"));
            addToBasketErrorsCounter.Add(1);
            ThrowNotAuthenticated();
        }

        activity?.SetTag("user.id", userId);
        activity?.SetTag("basket.access_time", DateTime.UtcNow);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin UpdateBasket call from method {Method} for basket id {Id}", context.Method, userId);
        }

        var customerBasket = MapToCustomerBasket(userId, request, activity);
        var response = await repository.UpdateBasketAsync(customerBasket);

        if (response is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Basket not found");
            activity?.AddEvent(new ActivityEvent("ERROR (UpdateBasket): Basket not found"));
            addToBasketErrorsCounter.Add(1);
            ThrowBasketDoesNotExist(userId);
        }

        addToBasketCounter.Add(1);

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.AddEvent(new ActivityEvent("SUCCESS: Basket updated with success"));

        return MapToCustomerBasketResponse(response, activity);
    }

    public override async Task<DeleteBasketResponse> DeleteBasket(DeleteBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        await repository.DeleteBasketAsync(userId);
        return new();
    }

    [DoesNotReturn]
    private static void ThrowNotAuthenticated() => throw new RpcException(new Status(StatusCode.Unauthenticated, "The caller is not authenticated."));

    [DoesNotReturn]
    private static void ThrowBasketDoesNotExist(string userId) => throw new RpcException(new Status(StatusCode.NotFound, $"Basket with buyer id {userId} does not exist"));

    private static CustomerBasketResponse MapToCustomerBasketResponse(CustomerBasket customerBasket, Activity activity)
    {
        var response = new CustomerBasketResponse();

        foreach (var item in customerBasket.Items)
        {
            response.Items.Add(new BasketItem()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });

            string eve = $"Updated item {item.ProductId} to {item.Quantity}";
            activity.AddEvent(new ActivityEvent(eve));
        }

        return response;
    }

    private static CustomerBasket MapToCustomerBasket(string userId, UpdateBasketRequest customerBasketRequest, Activity activity)
    {

        var response = new CustomerBasket
        {
            BuyerId = userId    
        };

        foreach (var item in customerBasketRequest.Items)
        {
            response.Items.Add(new()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
            addToBasketItemsAddedCounter.Add(item.Quantity);
            string tag = $"basket.item.{item.ProductId}";
            activity.SetTag(tag, item.Quantity);
        }

        return response;
    }
}