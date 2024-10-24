using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using FluentValidation;
using FluentValidation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configurar a conexão com o MongoDB usando Injeção de Dependência
builder.Services.AddSingleton<IMongoClient, MongoClient>(sp => new MongoClient("mongodb://localhost:27017"));
builder.Services.AddScoped(sp => sp.GetService<IMongoClient>().GetDatabase("MinimalApiMongo"));
builder.Services.AddScoped(sp => sp.GetService<IMongoDatabase>().GetCollection<Item>("Items"));
builder.Services.AddFluentValidation(fv => fv.RegisterValidatorsFromAssemblyContaining<ItemValidator>());

var app = builder.Build();

// Criar Índices
app.Services.GetService<IMongoCollection<Item>>()?.Indexes.CreateOne(new CreateIndexModel<Item>(
    Builders<Item>.IndexKeys.Ascending(item => item.Name).Ascending(item => item.Price)));

// Endpoints
app.MapGet("/items", async (IMongoCollection<Item> collection) =>
{
    var items = await collection.Find(new BsonDocument()).ToListAsync();
    return Results.Ok(items);
});

app.MapGet("/items/{id}", async (IMongoCollection<Item> collection, string id) =>
{
    var item = await collection.Find(i => i.Id == id).FirstOrDefaultAsync();
    return item != null ? Results.Ok(item) : Results.NotFound();
});

app.MapPost("/items", async (IMongoCollection<Item> collection, Item item) =>
{
    await collection.InsertOneAsync(item);
    return Results.Created($"/items/{item.Id}", item);
});

app.MapPut("/items/{id}", async (IMongoCollection<Item> collection, string id, Item updatedItem) =>
{
    var result = await collection.ReplaceOneAsync(i => i.Id == id, updatedItem);
    return result.MatchedCount > 0 ? Results.NoContent() : Results.NotFound();
});

app.MapDelete("/items/{id}", async (IMongoCollection<Item> collection, string id) =>
{
    var result = await collection.DeleteOneAsync(i => i.Id == id);
    return result.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/items/search", async (IMongoCollection<Item> collection, string? name, double? minPrice, double? maxPrice) =>
{
    var filterBuilder = Builders<Item>.Filter;
    var filters = new List<FilterDefinition<Item>>();

    if (!string.IsNullOrEmpty(name))
    {
        filters.Add(filterBuilder.Regex("Name", new BsonRegularExpression(name, "i")));
    }

    if (minPrice.HasValue)
    {
        filters.Add(filterBuilder.Gte(item => item.Price, minPrice.Value));
    }

    if (maxPrice.HasValue)
    {
        filters.Add(filterBuilder.Lte(item => item.Price, maxPrice.Value));
    }

    var combinedFilter = filterBuilder.And(filters);
    var items = await collection.Find(combinedFilter).ToListAsync();
    return Results.Ok(items);
});

app.Run();


// Classe Item
public class Item
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public double Price { get; set; }
}

// Validação com FluentValidation
public class ItemValidator : AbstractValidator<Item>
{
    public ItemValidator()
    {
        RuleFor(item => item.Name).NotEmpty().WithMessage("O nome é obrigatório.");
        RuleFor(item => item.Price).GreaterThan(0).WithMessage("O preço deve ser maior que zero.");
    }
}