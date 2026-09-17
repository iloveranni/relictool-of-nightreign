using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NightreignRelicTool.CustomEffectSearch;
using NightreignRelicTool.Localization;
namespace NightreignRelicTool.FinalUI {
 // #1: preserve card borders and all unchanged subtrees, with no empty intermediate layout.
 internal sealed class ResultRetention {
  object[] tabs=new object[0]; int tabLanguage=-1;
  readonly Dictionary<CustomEffectRelicSlotViewModel,Entry> cards=new Dictionary<CustomEffectRelicSlotViewModel,Entry>();
  class Entry {public Border Border;public CustomEffectRelicCandidate Candidate;public int Language;}
  public bool TabsChanged(IEnumerable<CustomEffectVesselResultViewModel> values,int language){var a=values.Cast<object>().ToArray();bool changed=tabLanguage!=language||!tabs.SequenceEqual(a);tabs=a;tabLanguage=language;return changed;}
  public void Update(MainWindow w){
   var slots=w.Model.SelectedResult==null?new CustomEffectRelicSlotViewModel[0]:w.Model.SelectedResult.Slots.OrderBy(s=>s.DisplayRow).ThenBy(s=>s.DisplayColumn).ToArray();
   int language=LocalizationService.Current.Revision;
   for(int i=0;i<slots.Length;i++){
    var slot=slots[i];Entry entry;
    if(!cards.TryGetValue(slot,out entry)){entry=new Entry{Border=w.CreateResultCard(slot),Candidate=slot.Current,Language=language};cards.Add(slot,entry);}
    else if(!ReferenceEquals(entry.Candidate,slot.Current)||entry.Language!=language){var fresh=w.CreateResultCard(slot);var content=fresh.Child;fresh.Child=null;entry.Border.Child=content;entry.Candidate=slot.Current;entry.Language=language;}
    if(i>=w.ResultGrid.Children.Count)w.ResultGrid.Children.Add(entry.Border);
    else if(!ReferenceEquals(w.ResultGrid.Children[i],entry.Border)){w.ResultGrid.Children.RemoveAt(i);w.ResultGrid.Children.Insert(i,entry.Border);}
    var grid=(Grid)entry.Border.Child;var actions=grid.Children.OfType<DockPanel>().Single();
    ((Button)actions.Children[0]).IsEnabled=slot.CanPrevious&&w.Model.CanShowPosition;
    ((Button)actions.Children[1]).IsEnabled=slot.CanNext&&w.Model.CanShowPosition;
   }
   while(w.ResultGrid.Children.Count>slots.Length)w.ResultGrid.Children.RemoveAt(w.ResultGrid.Children.Count-1);
   foreach(var old in cards.Keys.Where(s=>!slots.Contains(s)).ToArray())cards.Remove(old);
  }
 }
}
