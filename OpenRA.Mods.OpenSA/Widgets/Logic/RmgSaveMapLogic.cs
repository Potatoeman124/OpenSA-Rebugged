#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class RmgSaveMapLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public RmgSaveMapLogic(Widget widget, Func<bool> saveDisabled, Action<string> onSave)
		{
			var name = widget.Get<TextFieldWidget>("MAP_NAME");
			var save = widget.Get<ButtonWidget>("SAVE_BUTTON");
			var cancel = widget.Get<ButtonWidget>("CANCEL_BUTTON");
			var error = string.Empty;
			name.MaxLength = RmgMapSaver.MaximumNameLength;
			name.OnTextEdited = () => error = string.Empty;
			widget.Get<LabelWidget>("ERROR").GetText = () => saveDisabled() ? "The preview changed. Close this dialog and generate it again." : error;
			save.IsDisabled = () => saveDisabled() || RmgMapSaver.NameError(name.Text) != null;
			save.OnClick = () =>
			{
				if (save.IsDisabled()) return;
				try
				{
					onSave(name.Text.Trim());
					Ui.CloseWindow();
				}
				catch (Exception e)
				{
					error = string.IsNullOrWhiteSpace(e.Message) ? "The map could not be saved. Please try again." :
						e.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
					Log.Write("debug", "RMG map save failed:");
					Log.Write("debug", e);
				}
			};
			cancel.OnClick = Ui.CloseWindow;
			name.OnEnterKey = _ => { save.OnClick(); return true; };
			name.OnEscKey = _ => { cancel.OnClick(); return true; };
			name.TakeKeyboardFocus();
		}
	}
}
