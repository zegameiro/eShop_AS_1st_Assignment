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

    private static readonly Counter<long> getBasketCounter = meter.CreateCounter<long>("basket.get_basket.count");
    private static readonly Counter<long> updateBasketCounter = meter.CreateCounter<long>("basket.update_basket.count");
    private static readonly Counter<long> updateBasketItemsAddedCounter = meter.CreateCounter<long>("basket.update_basket.items_added");
    private static readonly Counter<long> updateBasketErrorsCounter = meter.CreateCounter<long>("basket.update_basket.errors.count");
    private static readonly Counter<long> getBasketErrorsCounter = meter.CreateCounter<long>("basket.get_basket.errors.count");
    private static readonly Counter<long> deleteBasketCounter = meter.CreateCounter<long>("basket.delete_basket.count");

    [AllowAnonymous]
    public override async Task<CustomerBasketResponse> GetBasket(GetBasketRequest request, ServerCallContext context)
    {

        using var activity = activitySource.StartActivity("GetBasket", ActivityKind.Server);
        
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "User not authenticated");
            getBasketErrorsCounter.Add(1);
            return new();
        }

        activity.SetTag("basket.user_id", userId);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin GetBasketById call from method {Method} for basket id {Id}", context.Method, userId);
        }

        var data = await repository.GetBasketAsync(userId);
        getBasketCounter.Add(1);

        if (data is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return MapToCustomerBasketResponse(data);
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
            ThrowNotAuthenticated();
        }

        activity?.SetTag("basket.user_id", userId);
        activity?.SetTag("basket.item_count", request.Items.Count);
        activity?.SetTag("basket.timestamp", DateTime.UtcNow);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin UpdateBasket call from method {Method} for basket id {Id}", context.Method, userId);
        }

        var customerBasket = MapToCustomerBasket(userId, request);
        var response = await repository.UpdateBasketAsync(customerBasket);
        updateBasketCounter.Add(1);
        updateBasketItemsAddedCounter.Add(request.Items.Count);

        if (response is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Basket not found");
            updateBasketErrorsCounter.Add(1);
            ThrowBasketDoesNotExist(userId);
        }

        activity?.SetStatus(ActivityStatusCode.Ok);

        return MapToCustomerBasketResponse(response);
    }

    public override async Task<DeleteBasketResponse> DeleteBasket(DeleteBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        await repository.DeleteBasketAsync(userId);
        deleteBasketCounter.Add(1);
        return new();
    }

    [DoesNotReturn]
    private static void ThrowNotAuthenticated() => throw new RpcException(new Status(StatusCode.Unauthenticated, "The caller is not authenticated."));

    [DoesNotReturn]
    private static void ThrowBasketDoesNotExist(string userId) => throw new RpcException(new Status(StatusCode.NotFound, $"Basket with buyer id {userId} does not exist"));

    private static CustomerBasketResponse MapToCustomerBasketResponse(CustomerBasket customerBasket)
    {
        var response = new CustomerBasketResponse();

        foreach (var item in customerBasket.Items)
        {
            response.Items.Add(new BasketItem()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }

        return response;
    }

    private static CustomerBasket MapToCustomerBasket(string userId, UpdateBasketRequest customerBasketRequest)
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
        }

        return response;
    }
}