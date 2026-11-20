using ZtpLab03.Cart.Infrastructure;
using ZtpLab03.Orders.Infrastructure;
using ZtpLab03.Products.Services;
using ZtpLab03.SharedKernel.CQRS;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Register the shared CQRS Mediator
builder.Services.AddSharedMediator();

// Register the Product Service module (exposed over HTTP at /api/products)
builder.Services.AddProductsModule();

// Determine Product Service HTTP base URL and PostgreSQL connection string
var productServiceBaseUrl = builder.Configuration["ProductService:BaseUrl"] ?? "http://localhost:5100";
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");

// Register the Cart Domain module (Clean Architecture: Domain, Application Use Cases, Infrastructure + PostgreSQL Event Store)
builder.Services.AddCartModule(productServiceBaseUrl, postgresConnectionString);

// Register the independent Order Domain module (Clean Architecture: Domain, Application Use Cases, Infrastructure + PostgreSQL)
builder.Services.AddOrdersModule(postgresConnectionString);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serve the interactive Web Presentation Dashboard from wwwroot/index.html at http://localhost:5100
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();

public partial class Program;
