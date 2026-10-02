// BagPageMath.cs
// 背包网格**分页口径**（用户指令 2026-10-02 第 2 条：格区改为均匀网格 + 最下面显示页数与翻页）——纯逻辑，无 Godot 依赖。
// 为什么单独一层：分页边界（0 件 / 恰好整页 / 末页剩几件 / 页码越界夹取）要能被 xUnit 直接断言，
// 而 `BagUi` 是 Godot `Control` 树（测试项目只引用构建好的 dll、不引用 GodotSharp），因此算术不能住在界面里。
// 界面只做两件事：按 `Columns` 铺 `PageCapacity` 个等宽格、把 `PageText` / 翻页可用性画出来。
using System;

public static class BagPageMath
{
	/// <summary>网格列数（均匀网格：5 列）。</summary>
	public const int Columns = 5;

	/// <summary>网格行数（均匀网格：5 行）。</summary>
	public const int Rows = 5;

	/// <summary>每页格数 = 列 × 行（= 25）。**固定**：空位也画格子，不是「有几件画几格」。</summary>
	public const int PageCapacity = Columns * Rows;

	/// <summary>分页数：0 件也算 1 页（空背包显示「第 1 / 1 页」，而不是 0 页）。</summary>
	public static int PageCountOf(int entryCount) =>
		entryCount <= 0 ? 1 : (entryCount + PageCapacity - 1) / PageCapacity;

	/// <summary>页码夹取到 `[0, 页数 - 1]`（页签切换 / 条目变少后照旧安全）。</summary>
	public static int ClampPage(int page, int entryCount) =>
		Math.Clamp(page, 0, PageCountOf(entryCount) - 1);

	/// <summary>该页第一条在全量列表里的下标。</summary>
	public static int FirstIndexOn(int page, int entryCount) =>
		ClampPage(page, entryCount) * PageCapacity;

	/// <summary>该页实际有内容的格数（末页可能不满；空背包 = 0）。</summary>
	public static int CountOn(int page, int entryCount)
	{
		int remaining = entryCount - FirstIndexOn(page, entryCount);
		return remaining <= 0 ? 0 : Math.Min(remaining, PageCapacity);
	}

	/// <summary>是否还有上一页。</summary>
	public static bool HasPrevious(int page, int entryCount) => ClampPage(page, entryCount) > 0;

	/// <summary>是否还有下一页。</summary>
	public static bool HasNext(int page, int entryCount) =>
		ClampPage(page, entryCount) < PageCountOf(entryCount) - 1;

	/// <summary>页码文案：`第 {当前} / {总数} 页`（1 基，界面直接显示）。</summary>
	public static string PageText(int page, int entryCount) =>
		$"第 {ClampPage(page, entryCount) + 1} / {PageCountOf(entryCount)} 页";
}
