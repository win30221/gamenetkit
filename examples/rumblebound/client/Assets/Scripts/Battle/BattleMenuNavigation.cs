using System;

namespace Rumblebound.Example.Battle
{
    public enum BattleMenuPage { None, Session, Settings, Technical }

    public sealed class BattleMenuNavigation
    {
        public BattleMenuPage Page { get; private set; }
        public bool InGame { get; private set; }
        public bool ConfirmDiscard { get; private set; }
        public bool IsOpen => Page != BattleMenuPage.None;

        public void Open(BattleMenuPage page, bool inGame)
        {
            CheckPage(page, inGame);Page=page;InGame=inGame;ConfirmDiscard=false;
        }
        public void Select(BattleMenuPage page)
        {
            if(!IsOpen)throw new InvalidOperationException("Open the menu before selecting a page");
            CheckPage(page, InGame);Page=page;ConfirmDiscard=false;
        }
        private static void CheckPage(BattleMenuPage page,bool inGame)
        {
            if(page!=BattleMenuPage.Session&&page!=BattleMenuPage.Settings&&page!=BattleMenuPage.Technical||page==BattleMenuPage.Session&&!inGame)throw new ArgumentOutOfRangeException(nameof(page));
        }
        public bool Back(bool hasChanges)
        {
            if(ConfirmDiscard){ConfirmDiscard=false;return false;}
            if(!IsOpen)return false;
            if(!InGame&&Page==BattleMenuPage.Technical){Page=BattleMenuPage.Settings;return false;}
            return RequestClose(hasChanges);
        }
        public bool RequestClose(bool hasChanges)
        {
            if(!IsOpen)return true;
            if(hasChanges){ConfirmDiscard=true;return false;}
            Reset();return true;
        }
        public void KeepEditing()=>ConfirmDiscard=false;
        public void Reset(){Page=BattleMenuPage.None;InGame=false;ConfirmDiscard=false;}
    }
}
