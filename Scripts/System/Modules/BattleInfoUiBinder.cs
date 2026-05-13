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

    public void BindTabButtons(Node scene, string runtimePath, string runtimeFallbackPath, string drawPath, string drawFallbackPath, string discardPath, string discardFallbackPath, Action onRuntimePressed, Action onDrawPressed, Action onDiscardPressed, BattleSytem.BattleInfoTab currentTab)
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

        UpdateTabVisualState(scene, runtimePath, runtimeFallbackPath, drawPath, drawFallbackPath, discardPath, discardFallbackPath, currentTab);
    }

    public void UpdateTabVisualState(Node scene, string runtimePath, string runtimeFallbackPath, string drawPath, string drawFallbackPath, string discardPath, string discardFallbackPath, BattleSytem.BattleInfoTab currentTab)
    {
        Button runtimeButton = FindBattleInfoButton(scene, runtimePath, runtimeFallbackPath);
        Button drawPileButton = FindBattleInfoButton(scene, drawPath, drawFallbackPath);
        Button discardPileButton = FindBattleInfoButton(scene, discardPath, discardFallbackPath);

        UpdateButtonState(runtimeButton, currentTab == BattleSytem.BattleInfoTab.Runtime);
        UpdateButtonState(drawPileButton, currentTab == BattleSytem.BattleInfoTab.DrawPile);
        UpdateButtonState(discardPileButton, currentTab == BattleSytem.BattleInfoTab.DiscardPile);
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