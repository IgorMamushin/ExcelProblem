using ExcelDownloadProblem;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.ConfigureSwaggerGen(o =>
{
    o.OrderActionsBy(x => x.RelativePath);

    o.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            Implicit = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri("http://localhost/"),
                TokenUrl = new Uri("http://localhost/"),
            }
        },
    });

    o.OperationFilter<SecurityOperationFilter>();
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = "Some";
        o.Audience = "Some";
        o.RequireHttpsMetadata = false;
        o.SaveToken = true;
        o.TokenValidationParameters.NameClaimType = "name";
        o.TokenValidationParameters.ValidateAudience = false;
    });

builder.Services
    .Configure<KestrelServerOptions>(o => o.AllowSynchronousIO = true)
    ;

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(s =>
    {
        s.RouteTemplate = "{documentName}.json";
        s.PreSerializeFilters.Add(
            (document, request) =>
            {
                var serverUrl = $"http://localhost:5169";
                request.Headers.Add("test", new StringValues("swagger"));

                document.Servers = new List<OpenApiServer>
                {
                    new()
                    {
                        Url = serverUrl
                    }
                };
            });
    });
    
    app.MapWhen(context => context.Connection.LocalPort == 5170, app =>
    {
        app.UseSwaggerUI(/*o => o.SwaggerEndpoint("http://localhost:5169", "main")*/);
    });
    
    app.Use(
        (context, next) =>
        {
            context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
    
            return next.Invoke();
        });
    
    app.Use(
        (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/docs"))
            {
                return next.Invoke();
            }
    
            context.Response.Redirect("/swagger", true);
    
            return context.Response.CompleteAsync();
    
        });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();