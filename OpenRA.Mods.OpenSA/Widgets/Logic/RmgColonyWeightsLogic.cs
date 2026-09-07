#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class RmgColonyWeightsLogic : ChromeLogic
	{
		readonly List<Action> refresh = new();

		[ObjectCreator.UseCtor]
		public RmgColonyWeightsLogic(Widget widget, RmgColonyWeights initialWeights, Func<bool> configurationDisabled, Action<RmgColonyWeights> onApply)
		{
			var values = initialWeights.Values;
			var validators = new List<Func<bool>>();
			var width = Math.Min(900, Game.Renderer.Resolution.Width - 40);
			var height = Math.Min(530, Game.Renderer.Resolution.Height - 40);
			widget.Bounds = new Rectangle((Game.Renderer.Resolution.Width - width) / 2, (Game.Renderer.Resolution.Height - height) / 2, width, height);
			foreach (var id in new[] { "TITLE", "HELP", "NOTE", "SETTINGS" }) widget.Get(id).Bounds.Width = width - 40;
			widget.Get("SETTINGS").Bounds.Height = height - 180;
			widget.Get("NOTE").Bounds.Y = height - 89;
			foreach (var id in new[] { "RESET", "APPLY", "CANCEL" }) widget.Get(id).Bounds.Y = height - 43;
			widget.Get("APPLY").Bounds.X = width - 270;
			widget.Get("CANCEL").Bounds.X = width - 140;
			var panel = widget.Get<ScrollPanelWidget>("SETTINGS");
			var template = panel.Get("ROW");
			template.Bounds.Width = width - 64;
			var shrink = (900 - width) / 2;
			template.Get("SLIDER").Bounds.Width -= shrink;
			foreach (var id in new[] { "VALUE", "DEFAULT", "CHANCE" }) template.Get(id).Bounds.X -= shrink;
			template.Get("CHANCE").Bounds.Width = template.Bounds.Width - template.Get("CHANCE").Bounds.X - 10;
			panel.RemoveChildren();
			for (var i = 0; i < values.Length; i++)
			{
				var index = i;
				var row = template.Clone();
				row.IsVisible = () => true;
				panel.AddChild(row);
				var name = RmgColonyWeights.Keys[index];
				row.Get<LabelWidget>("LABEL").GetText = () => char.ToUpperInvariant(name[0]) + name[1..];
				var slider = row.Get<SliderWidget>("SLIDER");
				var field = row.Get<TextFieldWidget>("VALUE");
				var reset = row.Get<ButtonWidget>("DEFAULT");
				slider.MinimumValue = 0;
				slider.MaximumValue = 1000;
				slider.GetValue = () => values[index];
				slider.IsDisabled = configurationDisabled;
				slider.OnChange += value => { if (!configurationDisabled()) values[index] = Math.Clamp((int)Math.Round(value), 0, 1000); };
				string Display() => values[index].ToString(CultureInfo.InvariantCulture);
				field.Text = Display();
				field.IsDisabled = configurationDisabled;
				field.IsValid = () => int.TryParse(field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 && value <= 1000;
				field.OnTextEdited = () => { if (field.IsValid() && !configurationDisabled()) values[index] = int.Parse(field.Text, CultureInfo.InvariantCulture); };
				field.OnEnterKey = _ => { if (field.IsValid()) field.YieldKeyboardFocus(); return true; };
				field.OnLoseFocus = () => field.Text = Display();
				validators.Add(field.IsValid);
				refresh.Add(() => { if (!field.HasKeyboardFocus) field.Text = Display(); });
				reset.GetText = () => "Default (100)";
				reset.IsDisabled = configurationDisabled;
				reset.OnClick = () => { if (!configurationDisabled()) { values[index] = 100; field.Text = Display(); } };
				row.Get<LabelWidget>("CHANCE").GetText = () => values.Sum() == 0 ? "0% - disabled" : $"{100d * values[index] / values.Sum():0.##}%  ({values[index]} / {values.Sum()})";
			}

			widget.Get<LabelWidget>("NOTE").GetText = () => "Shares are selection probabilities, not exact quotas. Terrain capacity can limit placement. Starting colonies are unaffected.";
			var apply = widget.Get<ButtonWidget>("APPLY");
			apply.IsDisabled = () => configurationDisabled() || validators.Any(valid => !valid());
			apply.OnClick = () =>
			{
				if (apply.IsDisabled()) return;
				onApply(new RmgColonyWeights(values[0], values[1], values[2], values[3], values[4]));
				Ui.CloseWindow();
			};
			widget.Get<ButtonWidget>("CANCEL").OnClick = Ui.CloseWindow;
			widget.Get<ButtonWidget>("RESET").IsDisabled = configurationDisabled;
			widget.Get<ButtonWidget>("RESET").OnClick = () =>
			{
				if (configurationDisabled()) return;
				Array.Fill(values, 100);
				foreach (var action in refresh) action();
			};
		}

		public override void Tick()
		{
			foreach (var action in refresh) action();
		}
	}
}
