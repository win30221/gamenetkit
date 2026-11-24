using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDuel
{
    internal sealed class DuelMenu
    {
        private const float DesignHeight = 1000f;
        private const float MaxWidth = 520f;
        private const string DeckKey = "TowerDuel.Deck.v1";
        private readonly TowerDuelClient client;
        private readonly Action closeKeyboard;
        private readonly VisualElement shell, deckCards, catalogueRoot, settingsPanel, joinPanel,
            actionLayer, actionPanel, detailLayer, detailStage, detailStats, replaceBar;
        private readonly ScrollView battlePage, deckPage, detailScroll;
        private readonly Button battleTab, cardsTab, quickMatch, createRoom, joinRoom, joinConfirm,
            resetDeck, inspectButton, changeButton, detailChangeButton;
        private readonly TextField nameField, serverField, roomField;
        private readonly Label deckCount, deckGuide, status, catalogEmpty, replaceHint;
        private readonly Button catalogFilter;
        private readonly Label catalogFilterValue;
        private readonly VisualElement catalogFilterLayer, catalogFilterPanel;
        private readonly Button[] filterOptions;
        private readonly string[] filterValues={"全部","兵種","法術"};
        private string currentFilter="全部";
        private readonly Image detailSprite;
        private readonly DuelCharacterIdlePreview idlePreview;
        private readonly Button detailFormButton;
        private bool detailAirForm;
        private readonly CardTile[] slots = new CardTile[DuelRules.DeckSize];
        private readonly Dictionary<string, CardTile> catalogue = new Dictionary<string, CardTile>();
        private readonly List<string> deck = new List<string>(DuelRules.DeckSize);
        private readonly string[] defaults = new string[DuelRules.DeckSize];
        private string selectedCard, pendingAdd;
        private bool busy;
        private float cardWidth = 108f;
        [Serializable] private sealed class SavedDeck { public string[] cards; }

        public DuelMenu(VisualElement lobby, TowerDuelClient client, Action closeKeyboard)
        {
            this.client=client; this.closeKeyboard=closeKeyboard;
            shell=lobby.Q("menuShell");
            battlePage=lobby.Q<ScrollView>("battlePage"); deckPage=lobby.Q<ScrollView>("deckPage");
            deckCards=lobby.Q("deckCards"); catalogueRoot=lobby.Q("catalogCards");
            settingsPanel=lobby.Q("settingsPanel"); joinPanel=lobby.Q("joinPanel");
            actionLayer=lobby.Q("cardActionLayer"); actionPanel=lobby.Q("cardActionPanel");
            detailLayer=lobby.Q("cardDetailLayer"); detailStage=lobby.Q("detailStage");
            detailStats=lobby.Q("detailStats"); detailSprite=lobby.Q<Image>("detailSprite");
            idlePreview=new DuelCharacterIdlePreview(detailSprite,lobby.Q("detailShadow"));
            detailFormButton=lobby.Q<Button>("detailFormButton");
            detailFormButton.clicked+=ToggleDetailForm;
            detailScroll=lobby.Q<ScrollView>("detailScroll");
            replaceBar=lobby.Q("replaceBar"); replaceHint=lobby.Q<Label>("replaceHint");
            deckCount=lobby.Q<Label>("deckCount"); deckGuide=lobby.Q<Label>("deckGuide");
            catalogEmpty=lobby.Q<Label>("catalogEmpty"); status=lobby.Q<Label>("lobbyStatus");
            nameField=lobby.Q<TextField>("nameField"); serverField=lobby.Q<TextField>("serverField"); roomField=lobby.Q<TextField>("roomField");
            battleTab=lobby.Q<Button>("battleTab"); cardsTab=lobby.Q<Button>("cardsTab");
            quickMatch=lobby.Q<Button>("quickMatchButton"); createRoom=lobby.Q<Button>("createButton");
            joinRoom=lobby.Q<Button>("joinButton"); joinConfirm=lobby.Q<Button>("joinConfirmButton");
            resetDeck=lobby.Q<Button>("defaultDeckButton");
            inspectButton=lobby.Q<Button>("inspectCardButton"); changeButton=lobby.Q<Button>("changeCardButton");
            detailChangeButton=lobby.Q<Button>("detailChangeButton");
            catalogFilter=lobby.Q<Button>("catalogFilter");
            catalogFilterValue=lobby.Q<Label>("catalogFilterValue");
            catalogFilterLayer=lobby.Q("catalogFilterLayer");
            catalogFilterPanel=lobby.Q("catalogFilterPanel");
            filterOptions=new[]{lobby.Q<Button>("filterAllButton"),lobby.Q<Button>("filterTroopButton"),lobby.Q<Button>("filterSpellButton")};
            catalogFilter.clicked+=OpenFilter;
            for(int i=0;i<filterOptions.Length;i++)
            {
                string value=filterValues[i];
                filterOptions[i].clicked+=()=>SelectFilter(value);
            }
            catalogFilterLayer.RegisterCallback<PointerDownEvent>(evt=>{
                if(evt.target==catalogFilterLayer){CloseFilter();evt.StopPropagation();}
            });
            shell.RegisterCallback<KeyDownEvent>(evt=>{
                if(evt.keyCode==KeyCode.Escape&&catalogFilterLayer.style.display.value==DisplayStyle.Flex)
                {
                    CloseFilter();catalogFilter.Focus();evt.StopPropagation();
                }
            });
            nameField.value="玩家"+UnityEngine.Random.Range(10,99);
            serverField.value=client.Server;
#if UNITY_WEBGL && !UNITY_EDITOR
            serverField.style.display=DisplayStyle.None;
#endif
            for(int i=0;i<defaults.Length;i++)defaults[i]=client.Rules.cards[i].id;
            LoadDeck();
            BuildDeck();
            battleTab.clicked+=()=>ShowCards(false); cardsTab.clicked+=()=>ShowCards(true);
            resetDeck.clicked+=()=>{if(busy)return;deck.Clear();deck.AddRange(defaults);CompleteChange();};
            lobby.Q<Button>("settingsButton").clicked+=()=>ShowModal(settingsPanel);
            lobby.Q<Button>("settingsCloseButton").clicked+=CloseModals;
            lobby.Q<Button>("joinCancelButton").clicked+=CloseModals;
            createRoom.clicked+=()=>Connect("create");
            joinRoom.clicked+=()=>ShowModal(joinPanel);
            joinConfirm.clicked+=()=>Connect("join");
            roomField.RegisterValueChangedCallback(_=>RefreshControls());
            quickMatch.clicked+=()=>{if(client.Session.Searching)client.Leave();else Connect("quick");};
            lobby.Q<Button>("cancelReplace").clicked+=CancelReplace;
            inspectButton.clicked+=()=>{if(selectedCard!=null)ShowDetail(selectedCard);};
            changeButton.clicked+=ChangeSelected;
            detailChangeButton.clicked+=ChangeSelected;
            lobby.Q<Button>("detailCloseButton").clicked+=CloseDetail;
            lobby.Q<Button>("detailBackButton").clicked+=CloseDetail;
            actionLayer.RegisterCallback<PointerDownEvent>(evt=>{if(evt.target==actionLayer){CloseActions();evt.StopPropagation();}});
            detailLayer.RegisterCallback<PointerDownEvent>(evt=>{if(evt.target==detailLayer){CloseDetail();evt.StopPropagation();}});
            RefreshDeck();
            Display(settingsPanel,false); Display(joinPanel,false); Display(actionLayer,false);
            Display(detailLayer,false); Display(replaceBar,false); CloseFilter();
            Display(deckPage,false); Display(battlePage,true);
            DuelCartoonSkin.SetMenuTab(cardsTab,false); DuelCartoonSkin.SetMenuTab(battleTab,true);
        }

        public void Layout(Rect safeArea,float unitsPerPixel)
        {
            float scale=safeArea.height*unitsPerPixel/DesignHeight;
            if(scale<=0)return;
            float width=Mathf.Min(safeArea.width*unitsPerPixel/scale,MaxWidth);
            shell.style.width=width; shell.style.height=DesignHeight;
            shell.style.scale=new Scale(new Vector3(scale,scale,1));
            shell.style.left=safeArea.center.x*unitsPerPixel-width*scale/2;
            shell.style.top=(Screen.height-safeArea.yMax)*unitsPerPixel;
            cardWidth=Mathf.Min(154f,Mathf.Max(1f,(width-54f-32f)/4f));
            foreach(var row in deckCards.Children())SizeRow(row,cardWidth);
            foreach(var row in catalogueRoot.Children())SizeRow(row,cardWidth);
            // A compact header and a substantial illustration, without compressing the room actions.
            shell.Q(className:"menu-hero").style.height=468f;
            if(actionLayer.style.display.value==DisplayStyle.Flex)CloseActions();
            if(catalogFilterLayer.style.display.value==DisplayStyle.Flex)CloseFilter();
        }

        public void BlurInputs()
        {
            foreach(var field in new[]{nameField,serverField,roomField})
            {
                if(field.touchScreenKeyboard!=null)field.touchScreenKeyboard.active=false;
                field.Blur();
            }
        }
        public void Update()
        {
            idlePreview.Tick();
            var session=client.Session;
            busy=session.Searching||session.Connecting||session.Recovering;
            RefreshControls();
            string message=deck.Count!=DuelRules.DeckSize?"請選滿 8 張卡牌後再開始對戰。":session.Status;
            status.text=message; status.style.display=string.IsNullOrEmpty(message)?DisplayStyle.None:DisplayStyle.Flex;
            string text=session.Searching?"取消配對":"快速配對";
            var quickLabel=quickMatch.Q<Label>(className:"icon-label");
            if(quickLabel!=null){if(quickLabel.text!=text)quickLabel.text=text;}
            else if(quickMatch.text!=text)quickMatch.text=text;
        }
        private void RefreshControls()
        {
            bool full=deck.Count==DuelRules.DeckSize;
            deckCards.SetEnabled(!busy); catalogueRoot.SetEnabled(!busy);
            resetDeck.SetEnabled(!busy); catalogFilter.SetEnabled(!busy);
            if(busy&&catalogFilterLayer.style.display.value==DisplayStyle.Flex)CloseFilter();
            quickMatch.SetEnabled(client.Session.Searching||(!busy&&full));
            createRoom.SetEnabled(!busy&&full); joinRoom.SetEnabled(!busy&&full);
            joinConfirm.SetEnabled(!busy&&full&&!string.IsNullOrWhiteSpace(roomField.value));
            nameField.SetEnabled(!busy); roomField.SetEnabled(!busy); serverField.SetEnabled(!busy);
        }
        private static void Display(VisualElement element,bool show)=>element.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        private void ShowCards(bool show)
        {
            CloseModals(); CloseFilter(); CloseActions(); CloseDetail(); CancelReplace();
            Display(deckPage,show); Display(battlePage,!show);
            DuelCartoonSkin.SetMenuTab(cardsTab,show); DuelCartoonSkin.SetMenuTab(battleTab,!show);
        }
        // Use an in-panel menu instead of the unthemed runtime DropdownField
        // popup. All coordinates are in the same scaled menu shell as the cards.
        private void OpenFilter()
        {
            if(busy)return;
            CloseActions();
            for(int i=0;i<filterOptions.Length;i++)
                DuelCartoonSkin.SetFilterOption(filterOptions[i],filterValues[i]==currentFilter);
            Display(catalogFilterLayer,true);catalogFilterLayer.BringToFront();
            Rect anchor=catalogFilter.worldBound;
            var topRight=shell.WorldToLocal(new Vector2(anchor.xMax,anchor.yMin));
            var bottomRight=shell.WorldToLocal(new Vector2(anchor.xMax,anchor.yMax));
            float width=Mathf.Min(154f,Mathf.Max(1f,shell.resolvedStyle.width-24f));
            const float height=156f;
            float bottom=DesignHeight-100f;
            catalogFilterPanel.style.width=width;
            catalogFilterPanel.style.left=Mathf.Clamp(topRight.x-width,12f,Mathf.Max(12f,shell.resolvedStyle.width-width-12f));
            catalogFilterPanel.style.top=bottomRight.y+5f+height<=bottom
                ?bottomRight.y+5f:Mathf.Max(12f,topRight.y-height-5f);
            catalogFilter.EnableInClassList("catalog-filter-open",true);
            filterOptions[Array.IndexOf(filterValues,currentFilter)].Focus();
        }
        private void CloseFilter()
        {
            Display(catalogFilterLayer,false);
            catalogFilter.EnableInClassList("catalog-filter-open",false);
        }
        private void SelectFilter(string value)
        {
            currentFilter=value;catalogFilterValue.text=value;CloseFilter();RebuildCatalogue();
            catalogFilter.Focus();
        }
        private void ShowModal(VisualElement modal)
        {
            if(busy)return;
            CloseModals(); CloseFilter(); CloseActions(); CloseDetail(); Display(modal,true); modal.BringToFront();
        }
        private void CloseModals()
        {
            Display(settingsPanel,false); Display(joinPanel,false); closeKeyboard();
        }
        private void Connect(string mode)
        {
            if(busy||deck.Count!=DuelRules.DeckSize)return;
            if(mode=="join"&&string.IsNullOrWhiteSpace(roomField.value))return;
            CloseModals(); client.Connect(serverField.value,mode,mode=="join"?roomField.value.Trim():"",nameField.value,deck.ToArray());
        }

        private VisualElement NewRow(VisualElement parent)
        {
            var row=new VisualElement();row.AddToClassList("menu-card-row");
            row.style.flexShrink=0; row.style.height=cardWidth*4f/3f; row.style.minHeight=cardWidth*4f/3f;
            row.RegisterCallback<GeometryChangedEvent>(evt=>{
                if(evt.newRect.width<=0||Mathf.Abs(evt.newRect.width-evt.oldRect.width)<.1f)return;
                SizeRow(row,Mathf.Min(154f,Mathf.Max(1f,(evt.newRect.width-32f)/4f)));
            });
            parent.Add(row);return row;
        }
        private static void SizeRow(VisualElement row,float width)
        {
            float height=width*4f/3f;
            row.style.height=height;row.style.minHeight=height;row.style.flexShrink=0;
            foreach(var tile in row.Children())
            {
                tile.style.width=width;tile.style.height=height;tile.style.minHeight=height;tile.style.flexShrink=0;
            }
        }
        private void BuildDeck()
        {
            VisualElement row=null;
            for(int i=0;i<slots.Length;i++)
            {
                if(i%4==0)row=NewRow(deckCards);
                int index=i;var tile=new CardTile(()=>ChooseSlot(index));slots[i]=tile;
                row.Add(tile.Button);SizeRow(row,cardWidth);
            }
            foreach(var card in client.Rules.cards)
            {
                CardTile tile=null;string id=card.id;
                tile=new CardTile(()=>ChooseAvailable(id,tile.Button));tile.Show(card);
                catalogue.Add(id,tile);
            }
        }
        private void RebuildCatalogue()
        {
            foreach(var tile in catalogue.Values)tile.Button.RemoveFromHierarchy();
            catalogueRoot.Clear();VisualElement row=null;int count=0;
            foreach(var card in client.Rules.cards)
            {
                if(deck.Contains(card.id))continue;
                if(currentFilter=="兵種"&&card.IsSpell)continue;
                if(currentFilter=="法術"&&!card.IsSpell)continue;
                if(count++%4==0)row=NewRow(catalogueRoot);
                row.Add(catalogue[card.id].Button);
            }
            foreach(var item in catalogueRoot.Children())SizeRow(item,cardWidth);
            Display(catalogEmpty,count==0);
        }
        private void LoadDeck()
        {
            string json=PlayerPrefs.GetString(DeckKey,"");
            if(!string.IsNullOrEmpty(json))
            {
                try
                {
                    var saved=JsonUtility.FromJson<SavedDeck>(json)?.cards;
                    if(saved!=null&&saved.Length<=DuelRules.DeckSize)
                    {
                        var seen=new HashSet<string>();
                        bool valid=true;
                        foreach(string id in saved)if(client.Rules.Find(id)==null||!seen.Add(id)){valid=false;break;}
                        if(valid){deck.AddRange(saved);return;}
                    }
                }
                catch(ArgumentException){}
            }
            deck.AddRange(defaults);
        }
        private void RefreshDeck()
        {
            for(int i=0;i<slots.Length;i++)
            {
                slots[i].Show(i<deck.Count?client.Rules.Find(deck[i]):null);
                slots[i].Button.EnableInClassList("replace-target",pendingAdd!=null&&i<deck.Count);
            }
            deckCount.text=deck.Count+" / "+DuelRules.DeckSize;
            deckGuide.text=deck.Count==DuelRules.DeckSize?"每場對戰隨機洗牌":"還需加入 "+(DuelRules.DeckSize-deck.Count)+" 張卡牌";
            RebuildCatalogue(); RefreshControls();
        }
        private void CompleteChange()
        {
            PlayerPrefs.SetString(DeckKey,JsonUtility.ToJson(new SavedDeck{cards=deck.ToArray()}));PlayerPrefs.Save();
            CancelReplace(); CloseActions(); CloseDetail(); RefreshDeck();
        }

        private void ChooseSlot(int index)
        {
            if(busy)return;
            if(index>=deck.Count){CloseActions();return;}
            if(pendingAdd!=null)
            {
                if(!deck.Contains(pendingAdd)){deck[index]=pendingAdd;CompleteChange();}
                else CancelReplace();
                return;
            }
            OpenActions(deck[index],slots[index].Button);
        }
        private void ChooseAvailable(string id,Button tile)
        {
            if(busy||deck.Contains(id))return;
            CancelReplace();OpenActions(id,tile);
        }
        private void OpenActions(string id,VisualElement tile)
        {
            CloseActions(); selectedCard=id;
            tile.AddToClassList("menu-card-selected");
            UpdateChangeButtons(id);
            Display(actionLayer,true); actionLayer.BringToFront();
            var center=shell.WorldToLocal(tile.worldBound.center);
            float width=shell.resolvedStyle.width;
            float panelWidth=Mathf.Min(248f,width-20f);
            actionPanel.style.width=panelWidth;
            actionPanel.style.left=Mathf.Clamp(center.x-panelWidth/2,10f,Mathf.Max(10f,width-panelWidth-10f));
            var bottom=shell.WorldToLocal(new Vector2(tile.worldBound.center.x,tile.worldBound.yMax)).y;
            var top=shell.WorldToLocal(new Vector2(tile.worldBound.center.x,tile.worldBound.yMin)).y;
            actionPanel.style.top=bottom+82f<DesignHeight-100f?bottom+4f:Mathf.Max(8f,top-82f);
        }
        private void CloseActions()
        {
            Display(actionLayer,false);
            foreach(var tile in slots)if(tile!=null)tile.Button.RemoveFromClassList("menu-card-selected");
            foreach(var tile in catalogue.Values)tile.Button.RemoveFromClassList("menu-card-selected");
        }
        private void UpdateChangeButtons(string id)
        {
            bool remove=deck.Contains(id);
            foreach(var button in new[]{changeButton,detailChangeButton})
            {
                button.text=remove?"移出牌池":"加入牌池";
                button.EnableInClassList("remove-button",remove);
                button.EnableInClassList("add-button",!remove);
                DuelCartoonSkin.SetChangeButton(button,remove);
            }
        }
        private void ChangeSelected()
        {
            if(busy||selectedCard==null)return;
            string id=selectedCard;
            if(deck.Remove(id)){CompleteChange();return;}
            if(deck.Count<DuelRules.DeckSize){deck.Add(id);CompleteChange();return;}
            CloseActions(); CloseDetail();
            pendingAdd=id;
            replaceHint.text="選擇要替換的上場卡牌："+client.Rules.Find(id).name;
            Display(replaceBar,true);
            foreach(var tile in slots)tile.Button.AddToClassList("replace-target");
            deckPage.scrollOffset=Vector2.zero;
        }
        private void CancelReplace()
        {
            pendingAdd=null;Display(replaceBar,false);
            foreach(var tile in slots)if(tile!=null)tile.Button.RemoveFromClassList("replace-target");
        }
        private void CloseDetail(){idlePreview.Stop();Display(detailLayer,false);}
        public void ClosePreview()=>CloseDetail();
        public void Dispose()=>idlePreview.Dispose();

        private void ToggleDetailForm()
        {
            if(selectedCard!="griffin")return;
            detailAirForm=!detailAirForm;
            detailFormButton.text=detailAirForm?"地面型態":"升空型態";
            idlePreview.Show(detailAirForm?"griffin-air":"griffin",detailAirForm);
        }
        private static string Number(float v)=>v.ToString("0.##");
        private static string Seconds(int ticks)=>Number(ticks/30f)+" 秒";
        private void ShowDetail(string id)
        {
            var card=client.Rules.Find(id);if(card==null)return;
            CloseActions(); selectedCard=id;
            detailLayer.Q<Label>("detailName").text=card.name;
            detailLayer.Q<Label>("detailCost").text=Number(card.cost);
            detailLayer.Q<Label>("detailType").text=card.IsSpell?"法術":(card.initialAir?"飛行兵種":card.target=="buildings"?"攻城兵種":"地面兵種");
            detailLayer.Q<Label>("detailDescription").text=card.description;
            detailStage.style.backgroundImage=DuelCartoonSkin.CharacterBackdrop(card.id,card.IsSpell);
            detailAirForm=false;
            Display(detailFormButton,id=="griffin");
            detailFormButton.text="升空型態";
            if(card.IsSpell)idlePreview.ShowSpell(DuelSpriteLibrary.Card(id));
            else idlePreview.Show(id,card.initialAir);
            var stats=new List<KeyValuePair<string,string>>();
            void Add(string title,string value)=>stats.Add(new KeyValuePair<string,string>(title,value));
            if(card.IsSpell)
            {
                if(card.damage>0)Add("傷害",card.damage.ToString());
                Add("作用半徑",Number(card.radius)+" 格");
                if(card.delayTicks>0)Add("落地延遲",Seconds(card.delayTicks));
                if(card.durationTicks>0)Add("持續時間",Seconds(card.durationTicks));
                if(card.knockbackDistance>0)Add("擊退距離",Number(card.knockbackDistance)+" 格");
                Add("可作用空中",card.hitAir?"是":"否");
            }
            else
            {
                Add("單位生命",card.hp.ToString());Add("單位攻擊",card.damage.ToString());
                Add("移動速度",Number(card.speed)+" 格/秒");Add("攻擊距離",Number(card.range)+" 格");
                Add("攻擊間隔",Seconds(card.cooldown));Add("出兵數量",card.count.ToString());
                Add("部署時間",Seconds(card.deployTicks));Add("攻擊空中",card.hitAir?"是":"否");
                if(card.splash>0)Add("範圍傷害",Number(card.splash)+" 格");
                if(card.transformAfterTicks>0)Add("升空時間",Seconds(card.transformAfterTicks));
                if(card.hpMultiplier>1)Add("生命倍率","×"+card.hpMultiplier);
                if(card.auraRadius>0)Add("增益半徑",Number(card.auraRadius)+" 格");
                if(card.moveMultiplier>1)Add("移速倍率","×"+Number(card.moveMultiplier));
            }
            detailStats.Clear();VisualElement row=null;
            for(int i=0;i<stats.Count;i++)
            {
                if(i%2==0){row=new VisualElement();row.AddToClassList("stat-row");detailStats.Add(row);}
                var cell=new VisualElement();cell.AddToClassList("stat-cell");
                var icon=new VisualElement{pickingMode=PickingMode.Ignore};icon.AddToClassList("stat-icon");
                DuelCartoonSkin.PaintStatIcon(icon,stats[i].Key);
                var copy=new VisualElement();copy.AddToClassList("stat-copy");
                var label=new Label(stats[i].Key);label.AddToClassList("stat-label");
                var value=new Label(stats[i].Value);value.AddToClassList("stat-value");
                copy.Add(label);copy.Add(value);cell.Add(icon);cell.Add(copy);row.Add(cell);
            }
            if(stats.Count%2!=0&&row!=null)
            {
                var spacer=new VisualElement{pickingMode=PickingMode.Ignore};
                spacer.AddToClassList("stat-cell");spacer.AddToClassList("stat-cell-spacer");row.Add(spacer);
            }
            UpdateChangeButtons(id);
            Display(detailLayer,true);detailLayer.BringToFront();detailScroll.scrollOffset=Vector2.zero;
        }

        private sealed class CardTile
        {
            public readonly Button Button;
            private readonly VisualElement art,frame;
            private readonly Label cost,empty,name;
            public CardTile(Action click)
            {
                Button=new Button(click);Button.AddToClassList("menu-card");
                art=new VisualElement{pickingMode=PickingMode.Ignore};art.AddToClassList("menu-card-art");Button.Add(art);
                frame=new VisualElement{pickingMode=PickingMode.Ignore};frame.AddToClassList("menu-card-frame");Button.Add(frame);
                DuelCartoonSkin.PaintCardFrame(frame);
                name=new Label{pickingMode=PickingMode.Ignore,enableRichText=false};name.AddToClassList("menu-card-name");Button.Add(name);
                cost=new Label{pickingMode=PickingMode.Ignore};cost.AddToClassList("menu-card-cost");Button.Add(cost);
                DuelCartoonSkin.PaintBadge(cost);
                empty=new Label("＋"){pickingMode=PickingMode.Ignore};empty.AddToClassList("empty-card-label");Button.Add(empty);
            }
            public void Show(CardDefinition card)
            {
                bool exists=card!=null;
                Button.EnableInClassList("empty-slot",!exists);
                art.style.backgroundImage=exists?new StyleBackground(DuelSpriteLibrary.Card(card.id)):new StyleBackground(StyleKeyword.None);
                cost.text=exists?Number(card.cost):""; name.text=exists?card.name:"";
                Display(cost,exists);Display(name,exists);Display(frame,exists);Display(empty,!exists);
                Button.tooltip=exists?card.name+" · "+cost.text+" 能量":"空的牌池欄位";
            }
        }
    }
}
