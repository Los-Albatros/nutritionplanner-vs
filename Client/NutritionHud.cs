using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace nutritionPlannerVintageStoryMod.Client;

public class NutritionHud : HudElement
{
    private static readonly double[] ColorGrain   = [0.765, 0.612, 0.180, 0.9];  // golden wheat
    private static readonly double[] ColorVeg     = [0.290, 0.580, 0.200, 0.9];  // forest green
    private static readonly double[] ColorProtein = [0.737, 0.200, 0.161, 0.9];  // brick red
    private static readonly double[] ColorDairy   = [0.420, 0.620, 0.820, 0.9];  // slate blue
    private static readonly double[] ColorFruit   = [0.900, 0.480, 0.100, 0.9];  // orange
    private static readonly double[] ColorAlert   = [0.860, 0.100, 0.100, 0.9];

    private readonly NutritionHudConfig _config;
    private readonly Action             _onSuggestRequest;

    private float _grain, _veg, _protein, _dairy, _fruit;
    private bool  _pulseGrain, _pulseVeg, _pulseProtein, _pulseDairy, _pulseFruit;
    private bool  _pulseToggle;

    private float _max               = 1500f;
    private long  _suggestCooldownEnd = -1;   // Environment.TickCount64 (ms), -1 = no cooldown
    private const long SuggestCooldownMs = 5000L;

    private readonly Dictionary<string, (bool belowT2, double lastMessageTime)> _alertState = new()
    {
        ["Grain"]   = (false, -9999),
        ["Veg"]     = (false, -9999),
        ["Protein"] = (false, -9999),
        ["Dairy"]   = (false, -9999),
        ["Fruit"]   = (false, -9999),
    };

    private string?    _suggestion;
    private double     _suggestionAge;
    private ItemStack? _suggestedStack;
    private DummySlot? _renderSlot;
    private double     _suggestionRelY;
    private const double SuggestionFadeSeconds = 30.0;

    private IReadOnlyList<FoodEntry> _history = [];
    private bool                     _showHistory;

    public NutritionHud(ICoreClientAPI api, NutritionHudConfig config,
        Action onSuggestRequest) : base(api)
    {
        _config           = config;
        _onSuggestRequest = onSuggestRequest;
    }

    public override string ToggleKeyCombinationCode => null!;
    public override double DrawOrder   => 0.2;
    public override bool   Focusable   => false;
    public override bool   PrefersUngrabbedMouse => false;

    public new void Toggle()
    {
        _config.HudVisible = !_config.HudVisible;
        if (_config.HudVisible) { _pulseToggle = false; Recompose(); TryOpen(); }
        else                      TryClose();
        _config.Save(capi);
    }

    public void SetSuggestion(string text, string? itemCode = null)
    {
        _suggestion     = text;
        _suggestionAge  = 0;
        _suggestedStack = MakeStack(itemCode);
        _renderSlot     = _suggestedStack != null ? new DummySlot(_suggestedStack) : null;
        if (IsOpened()) Recompose();
    }

    private ItemStack? MakeStack(string? code)
    {
        if (code == null) return null;
        var loc  = new AssetLocation(code);
        var item = capi.World.GetItem(loc);
        if (item  != null) return new ItemStack(item);
        var block = capi.World.GetBlock(loc);
        if (block != null) return new ItemStack(block, 1);
        return null;
    }

    public override void OnRenderGUI(float dt)
    {
        base.OnRenderGUI(dt);
        if (_renderSlot == null || _suggestionAge >= SuggestionFadeSeconds || SingleComposer == null) return;
        var sugg = SingleComposer.GetDynamicText("suggestion");
        if (sugg == null) return;
        double absX = sugg.Bounds.absX - GuiElement.scaled(36);
        double absY = sugg.Bounds.absY + GuiElement.scaled(1);
        capi.Render.RenderItemstackToGui(_renderSlot, absX, absY, 100, 20f, ColorUtil.WhiteArgb);
    }

    public void SetHistory(IReadOnlyList<FoodEntry> entries)
    {
        _history = entries;
        if (_showHistory && IsOpened()) Recompose();
    }

    public void ToggleHistory()
    {
        _showHistory = !_showHistory;
        if (IsOpened()) Recompose();
    }

    public void Refresh(float dtSeconds)
    {
        if (!_config.HudVisible) return;

        _suggestionAge += dtSeconds;
        _pulseToggle    = !_pulseToggle;

        var hunger = capi.World.Player?.Entity?.WatchedAttributes.GetTreeAttribute("hunger");
        if (hunger == null) return;

        float max = hunger.GetFloat("maxsaturation", 1500f);
        if (max <= 0) max = 1500f;
        _max     = max;
        _grain   = hunger.GetFloat("grainLevel")     / max * 100f;
        _veg     = hunger.GetFloat("vegetableLevel") / max * 100f;
        _protein = hunger.GetFloat("proteinLevel")   / max * 100f;
        _dairy   = hunger.GetFloat("dairyLevel")     / max * 100f;
        _fruit   = hunger.GetFloat("fruitLevel")     / max * 100f;

        _pulseGrain   = _grain   < _config.Threshold1;
        _pulseVeg     = _veg     < _config.Threshold1;
        _pulseProtein = _protein < _config.Threshold1;
        _pulseDairy   = _dairy   < _config.Threshold1;
        _pulseFruit   = _fruit   < _config.Threshold1;

        CheckAllThresholds();

        if (_suggestCooldownEnd > 0 && Environment.TickCount64 >= _suggestCooldownEnd)
            _suggestCooldownEnd = -1;

        if (!IsOpened()) { _pulseToggle = false; Recompose(); TryOpen(); }
        else UpdateBars();
    }

    private void CheckAllThresholds()
    {
        double now     = capi.World.Calendar.ElapsedSeconds;
        var    newCrit = new List<string>();

        foreach (var (name, pct) in new[]
        {
            ("Grain",   _grain),
            ("Veg",     _veg),
            ("Protein", _protein),
            ("Dairy",   _dairy),
            ("Fruit",   _fruit)
        })
        {
            var  state    = _alertState[name];
            bool nowBelow = pct < _config.Threshold2;

            if (nowBelow && !state.belowT2 && now - state.lastMessageTime >= _config.ChatCooldownSeconds)
                newCrit.Add($"{name} ({pct:F0}%)");

            _alertState[name] = (nowBelow, state.lastMessageTime);
        }

        if (newCrit.Count == 0) return;

        capi.ShowChatMessage($"[NutritionPlanner] {string.Join(" + ", newCrit)} critical! Consider eating.");
        foreach (var part in newCrit)
        {
            var name = part[..part.IndexOf(' ')];
            _alertState[name] = (true, now);
        }
        _onSuggestRequest();
    }

    private bool OnSuggestClick()
    {
        if (Environment.TickCount64 < _suggestCooldownEnd) return true;
        _suggestCooldownEnd = Environment.TickCount64 + SuggestCooldownMs;
        _onSuggestRequest();
        return true;
    }

    private void UpdateBars()
    {
        var composer = SingleComposer;
        if (composer == null) return;

        try
        {
            var barGrain   = composer.GetStatbar("bar-grain");
            var barVeg     = composer.GetStatbar("bar-veg");
            var barProtein = composer.GetStatbar("bar-protein");
            var barDairy   = composer.GetStatbar("bar-dairy");
            var barFruit   = composer.GetStatbar("bar-fruit");

            barGrain  ?.SetValue(_grain);
            barVeg    ?.SetValue(_veg);
            barProtein?.SetValue(_protein);
            barDairy  ?.SetValue(_dairy);
            barFruit  ?.SetValue(_fruit);

            composer.GetDynamicText("pct-grain")  ?.SetNewText($"{_grain:F0}%");
            composer.GetDynamicText("pct-veg")    ?.SetNewText($"{_veg:F0}%");
            composer.GetDynamicText("pct-protein")?.SetNewText($"{_protein:F0}%");
            composer.GetDynamicText("pct-dairy")  ?.SetNewText($"{_dairy:F0}%");
            composer.GetDynamicText("pct-fruit")  ?.SetNewText($"{_fruit:F0}%");

            if (_suggestion != null && _suggestionAge < SuggestionFadeSeconds)
                composer.GetDynamicText("suggestion")?.SetNewText($"→ {_suggestion}");
            else
                composer.GetDynamicText("suggestion")?.SetNewText("");
        }
        catch { Recompose(); }
    }

    private void Recompose()
    {
        _pulseToggle = false;  // always show normal colors on recompose — pulse resumes on next tick

        double pad    = GuiStyle.ElementToDialogPadding;
        double titleH = GuiStyle.TitleBarHeight;
        double rowH   = 22;
        double labelW = 52;
        double barW   = 130;
        double pctW   = 36;
        double innerW = labelW + 4 + barW + 4 + pctW;
        double suggH  = rowH * 2 + 4;
        double totalH = titleH + pad
            + 5 * rowH
            + 8
            + rowH
            + suggH
            + (_showHistory ? _history.Count * rowH + rowH : 0)
            + pad;

        var bgBounds  = ElementBounds.Fixed(0, 0, innerW + 2 * pad, totalH);
        var dlgBounds = ElementBounds.Fixed(EnumDialogArea.RightTop,
            -GuiStyle.DialogToScreenPadding, GuiStyle.DialogToScreenPadding,
            innerW + 2 * pad, totalH);

        SingleComposer = capi.Gui
            .CreateCompo("nutritionplanner-hud", dlgBounds)
            .AddShadedDialogBG(bgBounds, withTitleBar: true)
            .AddDialogTitleBar("Nutrition", () => Toggle())
            .BeginChildElements(bgBounds);

        var bars = new (string label, float val, float rawVal, string barKey, string pctKey, double[] color, int order)[]
        {
            ("Fruit",   _fruit,   _fruit   / 100f * _max, "bar-fruit",   "pct-fruit",   _pulseFruit   && _pulseToggle ? ColorAlert : ColorFruit,   0),
            ("Veg",     _veg,     _veg     / 100f * _max, "bar-veg",     "pct-veg",     _pulseVeg     && _pulseToggle ? ColorAlert : ColorVeg,     1),
            ("Grain",   _grain,   _grain   / 100f * _max, "bar-grain",   "pct-grain",   _pulseGrain   && _pulseToggle ? ColorAlert : ColorGrain,   2),
            ("Protein", _protein, _protein / 100f * _max, "bar-protein", "pct-protein", _pulseProtein && _pulseToggle ? ColorAlert : ColorProtein, 3),
            ("Dairy",   _dairy,   _dairy   / 100f * _max, "bar-dairy",   "pct-dairy",   _pulseDairy   && _pulseToggle ? ColorAlert : ColorDairy,   4),
        };

        double y = titleH + pad;
        foreach (var b in bars.OrderBy(b => b.val).ThenBy(b => b.order))
            AddBarRow(ref y, b.label, b.val, b.rawVal, _max, b.barKey, b.pctKey, b.color);

        y += 8;
        string btnLabel = "Suggest";
        var    btnBounds = ElementBounds.Fixed(pad + innerW - 80, y + 2, 80, rowH - 4);
        SingleComposer.AddSmallButton(btnLabel, OnSuggestClick, btnBounds, EnumButtonStyle.Small, "btn-suggest");
        y += rowH;

        _suggestionRelY = y;
        if (_suggestionAge >= SuggestionFadeSeconds) _suggestedStack = null;
        const double leftPad  = 5.0;
        var iconOffset = _suggestedStack != null ? 38.0 : 0.0;
        var suggText   = (_suggestion != null && _suggestionAge < SuggestionFadeSeconds) ? $"→ {_suggestion}" : "";
        var suggBounds = ElementBounds.Fixed(pad + leftPad + iconOffset, y, innerW - leftPad - iconOffset, suggH);
        SingleComposer.AddDynamicText(suggText, CairoFont.WhiteSmallText().WithFontSize(15f), suggBounds, "suggestion");
        y += suggH;

        if (_showHistory)
        {
            var hdrBounds = ElementBounds.Fixed(pad, y, innerW, rowH);
            SingleComposer.AddStaticText("Recent meals:", CairoFont.WhiteSmallText(), hdrBounds);
            y += rowH;
            foreach (var e in _history.TakeLast(5))
            {
                var eb = ElementBounds.Fixed(pad, y, innerW, rowH);
                SingleComposer.AddStaticText(
                    $"  {ShortCode(e.ItemCode)}: G+{e.DeltaGrain:F0} V+{e.DeltaVeg:F0} P+{e.DeltaProtein:F0} D+{e.DeltaDairy:F0} F+{e.DeltaFruit:F0}",
                    CairoFont.WhiteSmallText().WithFontSize(10f), eb);
                y += rowH;
            }
        }

        SingleComposer.EndChildElements().Compose();
    }

    private void AddBarRow(ref double y, string label, float value, float rawValue, float max,
        string barKey, string pctKey, double[] color)
    {
        double pad    = GuiStyle.ElementToDialogPadding;
        double rowH   = 22;
        double labelW = 52;
        double barW   = 130;
        double pctW   = 36;

        var lblBounds   = ElementBounds.Fixed(pad,                          y + 4, labelW,                       rowH - 4);
        var barBounds   = ElementBounds.Fixed(pad + labelW + 4,             y + 6, barW,                         rowH - 12);
        var pctBounds   = ElementBounds.Fixed(pad + labelW + 4 + barW + 4, y + 4, pctW,                         rowH - 4);
        var hoverBounds = ElementBounds.Fixed(pad,                          y,     labelW + 4 + barW + 4 + pctW, rowH);

        SingleComposer
            .AddStaticText(label, CairoFont.WhiteSmallText(), lblBounds)
            .AddStatbar(barBounds, color, barKey)
            .AddDynamicText($"{value:F0}%", CairoFont.WhiteSmallText(), pctBounds, pctKey)
            .AddHoverText($"{value:F0}%  ({rawValue:F0} / {max:F0})", CairoFont.WhiteSmallText(), 160, hoverBounds, $"hint-{barKey}");

        SingleComposer.GetStatbar(barKey)?.SetValue(value);
        y += rowH;
    }

    private static string ShortCode(string code)
    {
        var idx = code.LastIndexOf(':');
        return idx >= 0 ? code[(idx + 1)..] : code;
    }
}
