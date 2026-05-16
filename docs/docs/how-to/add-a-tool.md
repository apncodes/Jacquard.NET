---
sidebar_position: 1
---

# Add a Tool

The fastest way to give an agent a new capability is to add a `[Tool]`-decorated method to a `partial class`.

## Basic tool

```csharp
public partial class MyTools
{
    [Tool("Calculates the area of a rectangle")]
    public double CalculateArea(double width, double height) => width * height;
}
```

Pass it to the agent:

```csharp
var agent = new Agent(model, toolProviders: [new MyTools()]);
```

## Async tool

```csharp
public partial class DatabaseTools
{
    private readonly IDbConnection _db;

    public DatabaseTools(IDbConnection db) => _db = db;

    [Tool("Looks up a customer by ID")]
    public async Task<string> GetCustomer(string customerId)
    {
        var customer = await _db.QueryFirstOrDefaultAsync<Customer>(
            "SELECT * FROM customers WHERE id = @id", new { id = customerId });
        return customer?.ToString() ?? "Customer not found";
    }
}
```

## Tool with validation

```csharp
public partial class SearchTools
{
    [Tool("Searches the knowledge base")]
    public string Search(
        [ToolParameterValidation(Required = true, MinLength = 3, MaxLength = 200)]
        string query)
    {
        // query is guaranteed non-null, 3-200 chars
        return SearchIndex(query);
    }
}
```

## Multiple tools in one class

```csharp
public partial class WeatherTools
{
    [Tool("Returns current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";

    [Tool("Returns a 3-day forecast for a city")]
    public string GetForecast(string city) => $"Forecast for {city}: ...";

    [Tool("Converts temperature between Celsius and Fahrenheit")]
    public double ConvertTemp(double value, string from, string to) => /* ... */;
}
```

All three tools are registered when you pass `new WeatherTools()` to `toolProviders:`.
