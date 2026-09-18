#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Globalization;
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
			Func<bool> configurationDisabled, Action<int[], RmgColonyOwnershipMode> onApply, bool mandatoryIslandNests = false)
		{
			var mode = initialMode;
			var shares = (int[])initialShares.Clone();
			editor.Configure(widget, shares, Enumerable.Range(1, initialShares.Length).Select(i => $"Player {i} (lobby slot {i})").ToArray(),
				0, true, configurationDisabled, shares => onApply(shares, mode),
				() => mode == RmgColonyOwnershipMode.Random ? "randomly selected colonies" : "nearby colonies");
			if (mandatoryIslandNests) widget.Get<LabelWidget>("HELP").GetText = () => "Below 100: percentages; 100+: relative shares. Mandatory island nests are excluded and stay neutral.";
			var panel = widget.Get<ScrollPanelWidget>("SETTINGS");
			panel.Bounds.Y += 40;
			panel.Bounds.Height -= 40;
			var allValue = widget.Get<TextFieldWidget>("ALL_VALUE");
			allValue.Text = (shares.Distinct().Count() == 1 ? shares[0] : 0).ToString(CultureInfo.InvariantCulture);
			allValue.IsDisabled = configurationDisabled;
			allValue.IsValid = () => int.TryParse(allValue.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 && value <= 100;
			var setAll = widget.Get<ButtonWidget>("SET_ALL");
			setAll.IsDisabled = () => configurationDisabled() || !allValue.IsValid();
			setAll.OnClick = () =>
			{
				if (setAll.IsDisabled()) return;
				var value = int.Parse(allValue.Text, CultureInfo.InvariantCulture);
				Array.Fill(shares, value);
				foreach (var row in panel.Children)
					row.Get<TextFieldWidget>("VALUE").Text = value.ToString(CultureInfo.InvariantCulture);
			};
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
