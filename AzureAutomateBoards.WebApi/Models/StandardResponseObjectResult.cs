using Microsoft.AspNetCore.Mvc;

namespace AzureAutomateBoards.WebApi.Models;

public class StandardResponseObjectResult : ObjectResult
{
	public StandardResponseObjectResult(object value, int statusCode) : base(value)
	{
		StatusCode = statusCode;
	}
}
