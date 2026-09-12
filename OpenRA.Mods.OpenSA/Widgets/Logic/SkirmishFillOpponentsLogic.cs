#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class SkirmishFillOpponentsLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public SkirmishFillOpponentsLogic(Widget widget, ModData modData, OrderManager orderManager, bool skirmishMode)
		{
			var button = widget.Get<DropDownButtonWidget>("FILL_OPPONENTS");
			var slots = widget.Get<DropDownButtonWidget>("SLOTS_DROPDOWNBUTTON");
			var playersTab = widget.Get("SKIRMISH_TABS").Get<ButtonWidget>("PLAYERS_TAB");
			button.IsVisible = () => skirmishMode && !widget.Get("RMG_PANEL").IsVisible() && playersTab.IsHighlighted();
			button.IsDisabled = () => slots.IsDisabled() || orderManager.GameStarted ||
				orderManager.LocalClient == null || !orderManager.LocalClient.IsAdmin ||
				modData.MapCache[orderManager.LobbyInfo.GlobalSettings.Map].Status != MapStatus.Available ||
				!modData.MapCache[orderManager.LobbyInfo.GlobalSettings.Map].PlayerActorInfo.TraitInfos<IBotInfo>().Any() ||
				!orderManager.LobbyInfo.Slots.Any(s => s.Value.AllowBots &&
					(orderManager.LobbyInfo.ClientInSlot(s.Key) == null || orderManager.LobbyInfo.ClientInSlot(s.Key).Bot != null));

			button.OnMouseDown = _ =>
			{
				if (button.IsDisabled())
					return;

				var map = modData.MapCache[orderManager.LobbyInfo.GlobalSettings.Map];
				ScrollItemWidget Setup(IBotInfo bot, ScrollItemWidget template)
				{
					var item = ScrollItemWidget.Setup(template, () => false, () =>
					{
						// Recheck after opening the menu: the map or lobby state may have changed.
						if (button.IsDisabled() || !button.IsVisible() || orderManager.LobbyInfo.GlobalSettings.Map != map.Uid)
							return;

						foreach (var slot in orderManager.LobbyInfo.Slots)
						{
							var client = orderManager.LobbyInfo.ClientInSlot(slot.Key);
							if (slot.Value.AllowBots && (client == null || client.Bot != null))
								orderManager.IssueOrder(Order.Command($"slot_bot {slot.Key} {orderManager.LocalClient.Index} {bot.Type}"));
						}
					});
					item.Get<LabelWidget>("LABEL").GetText = () => bot.Name;
					return item;
				}

				button.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 200, map.PlayerActorInfo.TraitInfos<IBotInfo>(), Setup);
			};
		}
	}
}
