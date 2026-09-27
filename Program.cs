using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using TestCaseSDA.Models;
using TestCaseSDA.Services;

namespace TestCaseSDA;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                options.JsonSerializerOptions.WriteIndented = true;
            });
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressMapClientErrors = true;
            options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(
                ProcessResponse.Error("INVALID_REQUEST", "A JSON object with string parameters is required."));
        });
        builder.Services.AddScoped<IValidator<ProcessRequest>, ProcessRequestValidator>();
        builder.Services.AddScoped<PageProcessingService>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UseStatusCodePages(async context =>
        {
            var options = context.HttpContext.RequestServices
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<JsonOptions>>().Value;
            await context.HttpContext.Response.WriteAsJsonAsync(
                ProcessResponse.Error("INVALID_REQUEST", "The request could not be handled."),
                options.JsonSerializerOptions, context.HttpContext.RequestAborted);
        });
        app.UseSwagger(options => options.RouteTemplate = "api/swagger/{documentName}/swagger.json");
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "api/swagger";
            options.SwaggerEndpoint("/api/swagger/v1/swagger.json", "TestCaseSDA API");
        });

        app.MapControllers();

        app.Run();
    }
}
