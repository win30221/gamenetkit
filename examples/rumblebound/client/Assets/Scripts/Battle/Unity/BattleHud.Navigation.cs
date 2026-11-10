using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattleHud
    {
        private readonly BattleMenuNavigation navigation=new BattleMenuNavigation();
        private readonly Dictionary<string,Vector2> menuScroll=new Dictionary<string,Vector2>();
        private VisualElement navigationHeader,navigationPrompt;
        private Button resumeButton,applyOnlyButton;
        private bool resumeAfterTechnicalApply;
        private bool menuOpen=>navigation.Page==BattleMenuPage.Session;
        private bool settingsOpen=>navigation.Page==BattleMenuPage.Settings;
        private bool advanced=>navigation.Page==BattleMenuPage.Technical;
        private bool InGameContext=>Flow?.Room?.started==true&&!Flow.Battle.HasResult&&!Flow.Playback.Active;
        private bool HasTechnicalChanges=>selection!=null&&JsonUtility.ToJson(selection)!=JsonUtility.ToJson(Flow.Selection);
        private string ResumeText=>Flow.Room?.practice==true?"繼續練習":"繼續遊戲";
        private bool MenuWorking=>Flow.Busy||technicalReconnectPending||Flow.Battle.Connecting||Flow.Recovery.Pending;
        private string ResumeButtonText=>resumeAfterTechnicalApply?"正在套用並返回…":HasTechnicalChanges?"套用並"+ResumeText:!Flow.Battle.CanControl&&MenuWorking?"連線恢復中…":!Flow.Battle.CanControl?"重新連線並"+ResumeText:ResumeText;

        private void OpenMenuPage(BattleMenuPage page)
        {
            if(resumeAfterTechnicalApply)return;
            if(!navigation.IsOpen){selection=null;technicalNotice="";menuScroll.Clear();navigation.Open(page,InGameContext);}else navigation.Select(page);
            if(page==BattleMenuPage.Technical)selection=selection??Flow.Selection.Copy();
            MarkDirty();
        }
        private void CloseMenu()
        {
            navigation.Reset();selection=null;technicalNotice="";technicalReconnectPending=false;resumeAfterTechnicalApply=false;technicalStatus=null;resumeButton=null;applyOnlyButton=null;menuScroll.Clear();context=null;root?.Focus();MarkDirty();
        }
        private void ResumeGame(){uint generation=Flow.Battle.State.Generation;CloseMenu();Debug.Log("RF_BATTLE_MENU RESUME generation="+generation);}
        private void RequestResume()
        {
            if(MenuWorking)return;
            if(HasTechnicalChanges||!Flow.Battle.CanControl)ApplyTechnicalOptions(true);else ResumeGame();
        }
        private void BackMenu()
        {
            if(resumeAfterTechnicalApply)return;
            if(!navigation.IsOpen){if(InGameContext)OpenMenuPage(BattleMenuPage.Session);else if(lobbyPractice){lobbyPractice=false;MarkDirty();}return;}
            bool wasInGame=navigation.InGame;
            if(navigation.Back(HasTechnicalChanges)){if(wasInGame)ResumeGame();else CloseMenu();}else MarkDirty();
        }
        private void BuildSessionNavigation()
        {
            var header=Box("session-header-content");header.Add(Text(Flow.Room?.practice==true?"單人練習":"對戰選單","session-heading"));
            var tabs=Box("session-tabs");
            void Tab(string label,BattleMenuPage page){var button=Button(label,()=>OpenMenuPage(page),"technical-choice session-tab"+(navigation.Page==page?" selected":""),!resumeAfterTechnicalApply);button.name="menuTab"+page;tabs.Add(button);}
            Tab(Flow.Room?.practice==true?"練習選單":"對戰選單",BattleMenuPage.Session);Tab("設定",BattleMenuPage.Settings);Tab("框架選項",BattleMenuPage.Technical);header.Add(tabs);navigationHeader.Add(header);
        }
        private void BuildSessionFooter()
        {
            var footer=Box("footer session-footer");
            technicalStatus=Text(string.IsNullOrEmpty(technicalNotice)?HasTechnicalChanges?"設定尚未套用，切換分頁會保留變更。":"設定完成後可直接"+ResumeText+"；Esc 返回遊戲。":technicalNotice,"hint");footer.Add(technicalStatus);
            if(!string.IsNullOrEmpty(Flow.Notice))footer.Add(Text(Message(Flow.Notice),"status"));
            var actions=Box("menu-actions");if(advanced||HasTechnicalChanges){applyOnlyButton=Button("只套用設定",()=>ApplyTechnicalOptions(),"secondary",!MenuWorking&&HasTechnicalChanges);actions.Add(applyOnlyButton);}else applyOnlyButton=null;
            resumeButton=Button(ResumeButtonText,RequestResume,"resume-button",!MenuWorking);resumeButton.name="resumeGameButton";actions.Add(resumeButton);footer.Add(actions);dock.Add(footer);
        }
        private void RefreshNavigationPrompt()
        {
            if(navigationPrompt!=null){navigationPrompt.RemoveFromHierarchy();navigationPrompt=null;}
            if(!navigation.ConfirmDiscard)return;
            navigationPrompt=Box("result-backdrop");var dialog=Box("panel navigation-dialog");navigationPrompt.Add(dialog);
            dialog.Add(Text("設定尚未套用","title"));dialog.Add(Text("要先套用剛才修改的框架設定嗎？","muted"));
            dialog.Add(Button(navigation.InGame?"套用並"+ResumeText:"套用並返回",()=>ApplyTechnicalOptions(true),enabled:!MenuWorking));
            dialog.Add(Button(navigation.InGame?"捨棄變更並"+ResumeText:"捨棄變更並返回",()=>{if(navigation.InGame)ResumeGame();else CloseMenu();},"secondary",!MenuWorking));
            var keep=Button("留在設定",()=>navigation.KeepEditing(),"secondary");keep.name="keepEditingButton";dialog.Add(keep);modalLayer.Add(navigationPrompt);keep.Focus();
        }
    }
}
