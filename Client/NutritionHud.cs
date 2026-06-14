using nutritionPlannerVintageStoryMod.Network;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace nutritionPlannerVintageStoryMod.Client;

public class NutritionHud : HudElement
{
    private static readonly double[] ColorGrain   = [0.831, 0.627, 0.090, 0.9];
    private static readonly double[] ColorVeg     = [0.298, 0.686, 0.314, 0.9];
    private static readonly double[] ColorProtein = [0.937, 0.325, 0.314, 0.9];
    private static readonly double[] ColorDairy   = [0.392, 0.710, 0.965, 0.9];
    private static readonly double[] ColorAlert   = [0.900, 0.100, 0.100, 0.9];

    private readonly NutritionHudConfig               _config;
    private readonly Action<SuggestRequestPacket>     _sendSuggest;

    private float _grain, _veg, _protein, _dairy;
    private bool  _pulseGrain, _pulseVeg, _pulseProtein, _pulseDairy;
    private bool  _pulseToggle;

    private readonly Dictionary<string, (bool belowT2, double lastMessageTime)> _alertState = new()
    {
        ["Grain"]   = (false, -9999),
        ["Veg"]     = (false, -9999),
        ["Protein"] = (false, -9999),
        ["Dairy"]   = (false, -9999),
    };

    private string? _suggestion;
    private double  _suggestionAge;
    private const double SuggestionFadeSeconds = 30.0;

    private List<FoodEntryDto> _history = [];
    private bool               _showHistory;

    public NutritionHud(ICoreClientAPI api, NutritionHudConfig config,
        Action<SuggestRequestPacket> sendSuggest) : base(api)
    {
        _config      = config;
        _sendSuggest = sendSuggest;
    }

    public override string ToggleKeyCombinationCode => null!;
    public override double DrawOrder   => 0.2;
    public override bool   Focusable   => false;
    public override bool   PrefersUngrabbedMouse => false;

    public new void Toggle()
    {
        _config.HudVisible = !_config.HudVisible;
        if (_config.HudVisible) { Recompose(); TryOpen(); }
        else                      TryClose();
        _config.Save(capi);
    }

    public void SetSuggestion(string text)
    {
        _suggestion    = text;
        _suggestionAge = 0;
        if (IsOpened()) Recompose();
    }

    public void SetHistory(List<FoodEntryDto> entries)
    {
        _history = entries;
        if (_showHistory && IsOpened()) Recompose();
    }

    public void ToggleHistory()
    {
        _showHistory = !_showHistory;
        if (IsOpened()) Recompose();
    }

    public void ApplyConfigSync(ConfigSyncPacket p)
    {
        _config.Threshold1 = p.Threshold1;
        _config.Threshold2 = p.Threshold2;
    }

    public void Refresh(float dtSeconds)
    {
        if (!_config.HudVisible) return;

        _suggestionAge += dtSeconds;
        _pulseToggle    = !_pulseToggle;

        var beh = capi.World.Player.Entity.GetBehavior<EntityBehaviorHunger>();
        if (beh == null) return;

        float max = beh.MaxSaturation > 0 ? beh.MaxSaturation : 1500f;
        _grain   = beh.GrainLevel     / max * 100f;
        _veg     = beh.VegetableLevel / max * 100f;
        _protein = beh.ProteinLevel   / max * 100f;
        _dairy   = beh.DairyLevel     / max * 100f;

        _pulseGrain   = _grain   < _config.Threshold1;
        _pulseVeg     = _veg     < _config.Threshold1;
        _pulseProtein = _protein < _config.Threshold1;
        _pulseDairy   = _dairy   < _config.Threshold1;

        CheckThreshold2("Grain",   _grain);
        CheckThreshold2("Veg",     _veg);
        CheckThreshold2("Protein", _protein);
        CheckThreshold2("Dairy",   _dairy);

        if (!IsOpened()) { Recompose(); TryOpen(); }
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
                _sendSuggest(new SuggestRequestPacket());
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

            barGrain  ?.SetValue(_grain   / 100f);
            barVeg    ?.SetValue(_veg     / 100f);
            barProtein?.SetValue(_protein / 100f);
            barDairy  ?.SetValue(_dairy   / 100f);

            composer.GetDynamicText("pct-grain")  ?.SetNewText($"{_grain:F0}%");
            composer.GetDynamicText("pct-veg")    ?.SetNewText($"{_veg:F0}%");
            composer.GetDynamicText("pct-protein")?.SetNewText($"{_protein:F0}%");
            composer.GetDynamicText("pct-dairy")  ?.SetNewText($"{_dairy:F0}%");

            if (_suggestion != null && _suggestionAge < SuggestionFadeSeconds)
                composer.GetDynamicText("suggestion")?.SetNewText($"→ {_suggestion}");
            else
                composer.GetDynamicText("suggestion")?.SetNewText("");
        }
        catch { Recompose(); }
    }

    private void Recompose()
    {
        double pad    = GuiStyle.ElementToDialogPadding;
        double titleH = GuiStyle.TitleBarHeight;
        double rowH   = 22;
        double labelW = 52;
        double barW   = 120;
        double pctW   = 36;
        double innerW = labelW + 4 + barW + 4 + pctW;
        double totalH = titleH + pad
            + 4 * rowH
            + rowH
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

        var suggText  = (_suggestion != null && _suggestionAge < SuggestionFadeSeconds) ? $"→ {_suggestion}" : "";
        var suggBounds = ElementBounds.Fixed(pad, y, innerW, rowH);
        SingleComposer.AddDynamicText(suggText, CairoFont.WhiteSmallText().WithFontSize(11f), suggBounds, "suggestion");
        y += rowH;

        if (_showHistory)
        {
            var hdrBounds = ElementBounds.Fixed(pad, y, innerW, rowH);
            SingleComposer.AddStaticText("Recent meals:", CairoFont.WhiteSmallText(), hdrBounds);
            y += rowH;
            foreach (var e in _history.TakeLast(5))
            {
                var eb = ElementBounds.Fixed(pad, y, innerW, rowH);
                SingleComposer.AddStaticText(
                    $"  {ShortCode(e.ItemCode)}: G+{e.DeltaGrain:F0} V+{e.DeltaVeg:F0} P+{e.DeltaProtein:F0} D+{e.DeltaDairy:F0}",
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
        double rowH   = 22;
        double labelW = 52;
        double barW   = 120;
        double pctW   = 36;

        var lblBounds = ElementBounds.Fixed(pad,                          y + 4, labelW, rowH - 4);
        var barBounds = ElementBounds.Fixed(pad + labelW + 4,             y + 6, barW,   rowH - 12);
        var pctBounds = ElementBounds.Fixed(pad + labelW + 4 + barW + 4, y + 4, pctW,   rowH - 4);

        SingleComposer
            .AddStaticText(label, CairoFont.WhiteSmallText(), lblBounds)
            .AddStatbar(barBounds, color, barKey)
            .AddDynamicText($"{value:F0}%", CairoFont.WhiteSmallText(), pctBounds, pctKey);

        SingleComposer.GetStatbar(barKey)?.SetValue(value / 100f);
        y += rowH;
    }

    private static string ShortCode(string code)
    {
        var idx = code.LastIndexOf(':');
        return idx >= 0 ? code[(idx + 1)..] : code;
    }
}
