using Vintagestory.API.Client;

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

    private readonly Dictionary<string, (bool belowT2, double lastMessageTime)> _alertState = new()
    {
        ["Grain"]   = (false, -9999),
        ["Veg"]     = (false, -9999),
        ["Protein"] = (false, -9999),
        ["Dairy"]   = (false, -9999),
        ["Fruit"]   = (false, -9999),
    };

    private string? _suggestion;
    private double  _suggestionAge;
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

    public void SetSuggestion(string text)
    {
        _suggestion    = text;
        _suggestionAge = 0;
        if (IsOpened()) Recompose();
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

        CheckThreshold2("Grain",   _grain);
        CheckThreshold2("Veg",     _veg);
        CheckThreshold2("Protein", _protein);
        CheckThreshold2("Dairy",   _dairy);
        CheckThreshold2("Fruit",   _fruit);

        if (!IsOpened()) { _pulseToggle = false; Recompose(); TryOpen(); }
        else UpdateBars();
    }

    private void CheckThreshold2(string nutrient, float pct)
    {
        var state     = _alertState[nutrient];
        bool nowBelow = pct < _config.Threshold2;

        if (nowBelow && !state.belowT2)
        {
            double elapsed = capi.World.Calendar.ElapsedSeconds - state.lastMessageTime;
            if (elapsed >= _config.ChatCooldownSeconds)
            {
                capi.ShowChatMessage($"[NutritionPlanner] {nutrient} critical ({pct:F0}%). Consider eating.");
                _alertState[nutrient] = (true, capi.World.Calendar.ElapsedSeconds);
                _onSuggestRequest();
                return;
            }
        }

        _alertState[nutrient] = (nowBelow, state.lastMessageTime);
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
        double rowH   = 26;
        double labelW = 58;
        double barW   = 150;
        double pctW   = 40;
        double innerW = labelW + 4 + barW + 4 + pctW;
        double totalH = titleH + pad
            + 5 * rowH
            + 8                // spacer before button/suggestion
            + rowH             // suggest button row
            + rowH * 2         // suggestion text (two lines)
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

        double y = titleH + pad;
        AddBarRow(ref y, "Grain",   _grain,   "bar-grain",   "pct-grain",   _pulseGrain   && _pulseToggle ? ColorAlert : ColorGrain);
        AddBarRow(ref y, "Veg",     _veg,     "bar-veg",     "pct-veg",     _pulseVeg     && _pulseToggle ? ColorAlert : ColorVeg);
        AddBarRow(ref y, "Protein", _protein, "bar-protein", "pct-protein", _pulseProtein && _pulseToggle ? ColorAlert : ColorProtein);
        AddBarRow(ref y, "Dairy",   _dairy,   "bar-dairy",   "pct-dairy",   _pulseDairy   && _pulseToggle ? ColorAlert : ColorDairy);
        AddBarRow(ref y, "Fruit",   _fruit,   "bar-fruit",   "pct-fruit",   _pulseFruit   && _pulseToggle ? ColorAlert : ColorFruit);

        y += 8;
        var btnBounds = ElementBounds.Fixed(pad + innerW - 80, y + 2, 80, rowH - 4);
        SingleComposer.AddSmallButton("Suggest", () => { _onSuggestRequest(); return true; }, btnBounds, EnumButtonStyle.Small, "btn-suggest");
        y += rowH;

        var suggText   = (_suggestion != null && _suggestionAge < SuggestionFadeSeconds) ? $"→ {_suggestion}" : "";
        var suggBounds = ElementBounds.Fixed(pad, y, innerW, rowH * 2);
        SingleComposer.AddDynamicText(suggText, CairoFont.WhiteSmallText().WithFontSize(13f), suggBounds, "suggestion");
        y += rowH * 2;

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

    private void AddBarRow(ref double y, string label, float value,
        string barKey, string pctKey, double[] color)
    {
        double pad    = GuiStyle.ElementToDialogPadding;
        double rowH   = 26;
        double labelW = 58;
        double barW   = 150;
        double pctW   = 40;

        var lblBounds = ElementBounds.Fixed(pad,                          y + 4, labelW, rowH - 4);
        var barBounds = ElementBounds.Fixed(pad + labelW + 4,             y + 6, barW,   rowH - 12);
        var pctBounds = ElementBounds.Fixed(pad + labelW + 4 + barW + 4, y + 4, pctW,   rowH - 4);

        SingleComposer
            .AddStaticText(label, CairoFont.WhiteSmallText(), lblBounds)
            .AddStatbar(barBounds, color, barKey)
            .AddDynamicText($"{value:F0}%", CairoFont.WhiteSmallText(), pctBounds, pctKey);

        SingleComposer.GetStatbar(barKey)?.SetValue(value);
        y += rowH;
    }

    private static string ShortCode(string code)
    {
        var idx = code.LastIndexOf(':');
        return idx >= 0 ? code[(idx + 1)..] : code;
    }
}
