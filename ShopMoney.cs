using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopMoney
{
    public class ShopMoney : BasePlugin
    {
        public override string ModuleName => "[SHOP] Money";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        private IShopApi? SHOP_API;
        private const string CategoryName = "Money";
        public static JObject? JsonMoney { get; private set; }
        private readonly PlayerMoney[] playerMoney = new PlayerMoney[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/Money.json");
            if (File.Exists(configPath))
            {
                JsonMoney = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonMoney == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Доп. деньги");

            var sortedItems = JsonMoney
                .Properties()
                .Select(p => new { Key = p.Name, Value = (JObject)p.Value })
                .OrderBy(p => (int)p.Value["money"]!)
                .ToList();

            foreach (var item in sortedItems)
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(item.Key, (string)item.Value["name"]!, CategoryName, (int)item.Value["price"]!, (int)item.Value["sellprice"]!, (int)item.Value["duration"]!);
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerMoney[playerSlot] = null!);
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetNumberOfMoney(uniqueName, out int Money))
            {
                playerMoney[player.Slot] = new PlayerMoney(Money, itemId);
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'money' in config!");
            }
            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetNumberOfMoney(uniqueName, out int Money))
            {
                playerMoney[player.Slot] = new PlayerMoney(Money, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerMoney[player.Slot] = null!;
            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player != null && !player.IsBot && playerMoney[player.Slot] != null)
            {
                GiveMoney(player);
            }
            return HookResult.Continue;
        }

        private void GiveMoney(CCSPlayerController player)
        {
            var moneyServices = player.InGameMoneyServices;
            if (moneyServices == null) return;

            var money = playerMoney[player.Slot].Money;

            int maxmoney = ConVar.Find("mp_maxmoney")!.GetPrimitiveValue<int>();

            if (moneyServices.Account + money > maxmoney)
                moneyServices.Account = maxmoney;
            else
                moneyServices.Account += money;
        }

        private static bool TryGetNumberOfMoney(string uniqueName, out int Health)
        {
            Health = 0;
            if (JsonMoney != null && JsonMoney.TryGetValue(uniqueName, out var obj) && obj is JObject jsonItem && jsonItem["money"] != null && jsonItem["money"]!.Type != JTokenType.Null)
            {
                Health = (int)jsonItem["money"]!;
                return true;
            }
            return false;
        }

        public record PlayerMoney(int Money, int ItemID);
    }
}