#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class RmgColonyOwnershipLogic : ChromeLogic
	{
		readonly RmgColonyWeightsLogic editor = new();
		[ObjectCreator.UseCtor]
		public RmgColonyOwnershipLogic(Widget widget, int[] initialShares, RmgColonyOwnershipMode initialMode,
			Func<bool> configurationDisabled, Action<int[], RmgColonyOwnershipMode> onApply)
		{
			var mode = initialMode;
			editor.Configure(widget, (int[])initialShares.Clone(), Enumerable.Range(1, initialShares.Length).Select(i => $"Player {i} (lobby slot {i})").ToArray(),
				0, true, configurationDisabled, shares => onApply(shares, mode),
				() => mode == RmgColonyOwnershipMode.Random ? "randomly selected colonies" : "nearby colonies");
			var panel = widget.Get<ScrollPanelWidget>("SETTINGS");
			panel.Bounds.Y += 40;
			panel.Bounds.Height -= 40;
			var choice = widget.Get<DropDownButtonWidget>("OWNERSHIP_MODE");
			choice.GetText = () => RmgColonyOwnership.ModeDisplayName(mode);
			choice.IsDisabled = configurationDisabled;
			choice.OnMouseDown = _ =>
			{
				if (configurationDisabled()) return;
				ScrollItemWidget Setup(RmgColonyOwnershipMode value, ScrollItemWidget template)
				{
					var item = ScrollItemWidget.Setup(template, () => mode == value, () => { if (!configurationDisabled()) mode = value; });
					item.Get<LabelWidget>("LABEL").GetText = () => RmgColonyOwnership.ModeDisplayName(value);
					return item;
				}
				choice.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 50, Enum.GetValues<RmgColonyOwnershipMode>(), Setup);
			};
		}
		public override void Tick() => editor.Tick();
	}
}
