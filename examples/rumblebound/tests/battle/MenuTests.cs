using System;
using Rumblebound.Example.Battle;

public static class MenuTests
{
    private static void Require(bool value,string reason){if(!value)throw new Exception("Menu navigation: "+reason);}
    public static void Run()
    {
        var menu=new BattleMenuNavigation();
        foreach(var page in new[]{BattleMenuPage.Session,BattleMenuPage.Settings,BattleMenuPage.Technical}){
            menu.Open(BattleMenuPage.Session,true);menu.Select(page);
            Require(menu.Back(false)&&!menu.IsOpen,"one back action must resume from every in-game tab");
        }
        menu.Open(BattleMenuPage.Settings,false);menu.Select(BattleMenuPage.Technical);
        Require(!menu.Back(true)&&menu.Page==BattleMenuPage.Settings&&!menu.ConfirmDiscard,"outside a match, back from technical returns to settings without discarding a draft");
        Require(!menu.Back(true)&&menu.ConfirmDiscard,"leaving settings with a draft requires a choice");
        menu.KeepEditing();Require(menu.Page==BattleMenuPage.Settings&&!menu.ConfirmDiscard,"keep editing retains the current page");
        menu.Select(BattleMenuPage.Technical);menu.RequestClose(true);
        Require(!menu.Back(true)&&menu.IsOpen&&!menu.ConfirmDiscard,"escape dismisses the draft prompt without leaving settings");
        menu.RequestClose(false);Require(!menu.IsOpen,"explicit discard can return to the parent screen");
        menu.Open(BattleMenuPage.Session,true);menu.Select(BattleMenuPage.Technical);menu.Select(BattleMenuPage.Settings);
        Require(!menu.Back(true)&&menu.ConfirmDiscard,"dirty settings remain protected after switching tabs");
        menu.Reset();Require(!menu.IsOpen&&!menu.InGame&&!menu.ConfirmDiscard,"ending a match clears navigation and prompts");
        Require(menu.RequestClose(true)&&!menu.ConfirmDiscard,"a late close request cannot reopen a prompt after the match ended");
        bool rejected=false;try{menu.Open(BattleMenuPage.Session,false);}catch(ArgumentOutOfRangeException){rejected=true;}
        Require(rejected&&!menu.IsOpen,"session controls cannot open outside a match");
        Console.WriteLine("REMAKE_MENU PASS one-step in-game return, direct tabs, lobby back path, draft protection and match cleanup");
    }
}
