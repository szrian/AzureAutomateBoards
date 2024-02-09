using AzureAutomateBoards.WebApi.Misc;
using AzureAutomateBoards.WebApi.Models;
using AzureAutomateBoards.WebApi.Repos.Interfaces;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace AzureAutomateBoards.WebApi.Repos;

public class RulesRepo : IRulesRepo, IDisposable
{
	private IOptions<AppSettings> _appSettings;
	private IHelper _helper;
	private HttpClient _httpClient;

	public RulesRepo(IOptions<AppSettings> appSettings, IHelper helper, HttpClient httpClient)
	{
		_appSettings = appSettings;
		_helper = helper;
		_httpClient = httpClient;
	}

	public async Task<RulesModel> ListRules(string wit)
	{
		string src = _appSettings.Value.SourceForRules;

		var json = await _httpClient.GetStringAsync(src + "/rules." + wit.ToLower() + ".json");
		RulesModel rules = JsonConvert.DeserializeObject<RulesModel>(json);

		return rules;

	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	~RulesRepo()
	{
		// Finalizer calls Dispose(false)
		Dispose(false);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			_appSettings = null;
			_helper = null;
		}
	}
}
