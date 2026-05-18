using Godot;
using System;

internal sealed class BattleInfoUiBinder
{
    public RichTextLabel FindBattleInfoLabel(Node scene, string primaryPath, string fallbackPath)
    {
        if (scene == null)
        {
            return null;
        }

        RichTextLabel battleInfoLabel = scene.GetNodeOrNull<RichTextLabel>(primaryPath);
        if (battleInfoLabel != null)
        {
            return battleInfoLabel;
        }

        return scene.GetNodeOrNull<RichTextLabel>(fallbackPath);
    }

    public Button FindBattleInfoButton(Node scene, string primaryPath, string fallbackPath)
    {
        if (scene == null)
        {
            return null;
        }

        Button button = scene.GetNodeOrNull<Button>(primaryPath);
        if (button != null)
        {
            return button;
        }

        return scene.GetNodeOrNull<Button>(fallbackPath);
    }

    public void BindTabButtons(Node scene, string runtimePath, string runtimeFallbackPath, string drawPath, string drawFallbackPath, string discardPath, string discardFallbackPath, string exhaustPath, string exhaustFallbackPath, Action onRuntimePressed, Action onDrawPressed, Action onDiscardPressed, Action onExhaustPressed, BattleSytem.BattleInfoTab currentTab)
    {
        Button runtimeButton = FindBattleInfoButton(scene, runtimePath, runtimeFallbackPath);
        if (runtimeButton != null)
        {
            runtimeButton.Pressed += onRuntimePressed;
        }

        Button drawPileButton = FindBattleInfoButton(scene, drawPath, drawFallbackPath);
        if (drawPileButton != null)
        {
            drawPileButton.Pressed += onDrawPressed;
        }

        Button discardPileButton = FindBattleInfoButton(scene, discardPath, discardFallbackPath);
        if (discardPileButton != null)
        {
            discardPileButton.Pressed += onDiscardPressed;
        }

        Button exhaustPileButton = FindBattleInfoButton(scene, exhaustPath, exhaustFallbackPath);
        if (exhaustPileButton != null)
        {
            exhaustPileButton.Pressed += onExhaustPressed;
        }

        UpdateTabVisualState(scene, runtimePath, runtimeFallbackPath, drawPath, drawFallbackPath, discardPath, discardFallbackPath, exhaustPath, exhaustFallbackPath, currentTab);
    }

    public void UpdateTabVisualState(Node scene, string runtimePath, string runtimeFallbackPath, string drawPath, string drawFallbackPath, string discardPath, string discardFallbackPath, string exhaustPath, string exhaustFallbackPath, BattleSytem.BattleInfoTab currentTab)
    {
        Button runtimeButton = FindBattleInfoButton(scene, runtimePath, runtimeFallbackPath);
        Button drawPileButton = FindBattleInfoButton(scene, drawPath, drawFallbackPath);
        Button discardPileButton = FindBattleInfoButton(scene, discardPath, discardFallbackPath);
        Button exhaustPileButton = FindBattleInfoButton(scene, exhaustPath, exhaustFallbackPath);

        UpdateButtonState(runtimeButton, currentTab == BattleSytem.BattleInfoTab.Runtime);
        UpdateButtonState(drawPileButton, currentTab == BattleSytem.BattleInfoTab.DrawPile);
        UpdateButtonState(discardPileButton, currentTab == BattleSytem.BattleInfoTab.DiscardPile);
        UpdateButtonState(exhaustPileButton, currentTab == BattleSytem.BattleInfoTab.ExhaustPile);
    }

    private static void UpdateButtonState(Button button, bool isActive)
    {
        if (button == null)
        {
            return;
        }

        button.Disabled = isActive;
    }
}