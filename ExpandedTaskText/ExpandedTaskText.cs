using System.Diagnostics;
using System.Text;
using ExpandedTaskText.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;


namespace ExpandedTaskText;

// Load after EVERYTHING so all custom quests exist
[Injectable(TypePriority = int.MaxValue)]
public class ExpandedTaskText(
        ISptLogger<ExpandedTaskText> logger,
        LocaleTable localeTable,
        TradersTable tradersTable,
        TemplateTable templateTable,
        LocaleService localeService,
        FileUtil fileUtil,
        JsonUtil jsonUtil
    ) : IOnLoad
{
    private List<QuestInfo>? _questInfos;
    private Dictionary<MongoId, GunsmithInfo>? _gunsmithInfos;
    private EttConfig? _config;
    private string? _desiredServerLocale;

    private Dictionary<MongoId, string> _questDescriptionCache = [];
    
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var cachePath = Path.Combine(EttMetadata.ResourcesDirectory, "descriptionCache.json");

        _desiredServerLocale = localeService.GetDesiredServerLocale();
        
        if (File.Exists(cachePath))
        {
            var text = await File.ReadAllTextAsync(cachePath);
            _questDescriptionCache = jsonUtil.Deserialize<Dictionary<MongoId, string>>(text)!;
            
            logger.Info("[Expanded Task Text] loading please wait...");
        }
        else
        {
            logger.Info("[Expanded Task Text] First time loading, subsequent loading times will be significantly lower. Please wait...");
        }
        
        var questInfoText = await fileUtil.ReadFileAsync(Path.Combine(EttMetadata.ResourcesDirectory, "QuestInfo.json"));
        _questInfos = jsonUtil.Deserialize<List<QuestInfo>>(questInfoText);
        
        var gunsmithText = await fileUtil.ReadFileAsync(Path.Combine(EttMetadata.ResourcesDirectory, "GunsmithInfo.json"));
        _gunsmithInfos = jsonUtil.Deserialize<Dictionary<MongoId, GunsmithInfo>>(gunsmithText);
        
        var modConfig = await fileUtil.ReadFileAsync(Path.Combine(EttMetadata.ResourcesDirectory, "EttConfig.json"));
        _config = jsonUtil.Deserialize<EttConfig>(modConfig);
        
        await UpdateAllTaskText();
        
        var cacheText = jsonUtil.Serialize(_questDescriptionCache);
        await fileUtil.WriteFileAsync(cachePath, cacheText!);
        
        logger.Success($"[Expanded Task Text] Completed loading in {(sw.ElapsedMilliseconds / 1000f):F2} seconds.");
    }

    private Task UpdateAllTaskText()
    {
        var locales = localeService.GetLocaleDb(_desiredServerLocale);
        foreach (var info in _questInfos ?? [])
        {
            if (!locales.TryGetValue($"{info.Id.ToString()} description", out var description))
            {
                logger.Error($"[Expanded Task Text] Could not find quest description for `{info.Id.ToString()}`");
                continue;
            }

            foreach (var (_, globalLocales) in localeTable.Global)
            {
                UpdateTaskText(info, globalLocales, description);
            }
        }
        
        return Task.CompletedTask;
    }
    
    private void UpdateTaskText(QuestInfo info, LazyLoad<GlobalLocaleDictionary> locales, string originalDescription)
    {
        if (!_questDescriptionCache.TryGetValue(info.Id, out var newDescription))
        {
            _questDescriptionCache[info.Id] = BuildNewDescription(info, originalDescription);
        }
        
        locales.AddTransformer(transformer =>
        {
            transformer![$"{info.Id.ToString()} description"] = newDescription;
            
            return transformer;
        });
    }

    private string BuildNewDescription(QuestInfo info, string originalDescription)
    {
        var sb = new StringBuilder();
        if (_config == null)
        {
            throw new NullReferenceException("[Expanded Task Text] Attempted to read config.json, got null! Ensure the file exists and is valid.");
        }
        
        if (_config.DisplayAfterLore)
        {
            sb.Append(originalDescription);
        }

        sb.Append(info.KappaRequired
            ? "This quest is required for Collector\n"
            : "This quest is not required for Collector\n");

        sb.Append(info.LightkeeperRequired
            ? "This quest is required for Lightkeeper\n"
            : "This quest is not required for Lightkeeper\n");

        sb.Append(GetKeyInfoForQuest(info));
        sb.Append("\n\n");
        sb.Append(GetNextQuests(info.Id));
        sb.Append("\n\n");

        if (_gunsmithInfos?.TryGetValue(info.Id, out var gunsmithInfo) ?? false)
        {
            sb.Append(GetGunsmithPartsList(info.Id, gunsmithInfo));
            sb.Append("\n\n");
        }

        if (!_config.DisplayAfterLore)
        {
            sb.Append(originalDescription);
        }
        
        return sb.ToString();
    }

    private string GetNextQuests(MongoId currentQuestId)
    {
        var quests = templateTable.Quests;
        var result = new List<string>();
        
        foreach (var (qid, quest) in quests)
        {
            var availableForStart = quest.Conditions.AvailableForStart;
            
            if (availableForStart is null)
            {
                continue;
            }

            foreach (var condition in availableForStart)
            {
                if (condition.Target?.Item is null)
                {
                    continue;
                }
                
                if (condition.ConditionType == "Quest" && condition.Target.Item == currentQuestId.ToString())
                {
                    var nextQuestName = GetLocale($"{qid} name");
                    result.Add($"\n\t{nextQuestName}");
                }
            }
        }
        
        var sb = new StringBuilder();
        
        sb.Append(result.Count > 0 ? "Leads to:": "Leads to: Nothing");
        sb.Append(string.Join(", ", result));
        
        return sb.ToString();
    }

    private string GetKeyInfoForQuest(QuestInfo info)
    {
        var result = new List<string>();
        
        foreach (var obj in info.QuestObjectives)
        {
            if (obj.RequiredKeys is null)
            {
                continue;
            }
            
            foreach (var list in obj.RequiredKeys)
            {
                if (list is null)
                {
                    continue;
                }
                
                foreach (var key in list)
                {
                    var keyName = GetLocale($"{key.Id.ToString()} Name");
                    if (result.Contains(keyName) || string.IsNullOrEmpty(keyName))
                    {
                        continue;
                    }
                    result.Add($"\n\t{keyName}");
                }
            }
        }
        
        var sb = new StringBuilder();

        if (result.Count > 0)
        {
            sb.Append("Requires Key(s):");
            sb.Append(string.Join(", ", result));
        }
        else
        {
            sb.Append("No keys required.");
        }
        
        return sb.ToString();
    }

    private Dictionary<MongoId, int> GetAllTraderLoyalLevelItems()
    {
        var result = new Dictionary<MongoId, int>();

        foreach (var (_, trader) in tradersTable)
        {
            foreach (var (id, level) in trader?.Assort?.LoyalLevelItems ?? [])
            {
                result.TryAdd(id, level);
            }
        }
        
        return result;
    }
    
    private string GetGunsmithPartsList(MongoId mongoId, GunsmithInfo info)
    {
        var loyaltyLevelItems = GetAllTraderLoyalLevelItems();
        
        var sb = new StringBuilder();
        const string requiredDurability = "Required minimum gun durability: 60";
        sb.Append(requiredDurability);
        
        foreach (var partId in info.RequiredParts)
        {
            sb.Append($"\n{GetLocale($"{partId.ToString()} Name")}");

            foreach (var (tid, trader) in tradersTable)
            {
                if (trader?.Assort?.Items is null)
                {
                    continue;
                }
                
                var assortItems = trader.Assort.Items.ToArray();

                foreach (var item in assortItems)
                {
                    if (item.Template != partId)
                    {
                        continue;
                    }
                    
                    if (loyaltyLevelItems.TryGetValue(item.Id, out var loyaltyLevel))
                    {
                        var traderName = GetLocale($"{tid.ToString()} Nickname");
                        sb.Append($"\n\tSold by {traderName} LL {loyaltyLevel}");
                    }
                }
            }
        }

        return sb.ToString();
    }

    private string GetLocale(string key)
    {
        var locales = localeService.GetLocaleDb(_desiredServerLocale);

        if (!locales.TryGetValue(key, out var locale))
        {
            logger.Error($"[Expanded Task Text] Could not find locale for `{key}`");
            
            return string.Empty;
        }
        
        return locale;
    }
}