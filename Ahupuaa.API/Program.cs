using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Ahupuaa.API.Services;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure DynamoDB service
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddSingleton<IDynamoDBContext>(sp =>
{
    AmazonDynamoDBClient client = new AmazonDynamoDBClient();
#pragma warning disable CS0618 // Type or member is obsolete
    return new DynamoDBContext(client);
#pragma warning restore CS0618 // Type or member is obsolete
});

// Add caching
builder.Services.AddMemoryCache();

// Add services
builder.Services.AddScoped<IAhupuaaService, AhupuaaService>();


// Add controller with JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();