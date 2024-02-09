using AzureAutomateBoards.WebApi.Misc;
using AzureAutomateBoards.WebApi.Models;
using AzureAutomateBoards.WebApi.Repos.Interfaces;
using AzureAutomateBoards.WebApi.Repos;

var builder = WebApplication.CreateBuilder(args);

var config = builder.Configuration;
// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();

builder.Services.Configure<AppSettings>(config.GetSection("AppSettings"));
builder.Services.AddTransient<IHelper, Helper>();
builder.Services.AddTransient<IWorkItemRepo, WorkItemRepo>();
builder.Services.AddTransient<IRulesRepo, RulesRepo>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Home/Error");
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();
app.UseCors();

app.MapHealthChecks("/health");
app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
