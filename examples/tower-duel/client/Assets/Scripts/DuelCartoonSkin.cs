using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDuel
{
    // Fantasy B3 surfaces based on the approved asset palette and layered frame geometry.
    // Compact buttons use separate rivet-free textures; the card frame center is transparent.
    internal static class DuelCartoonSkin
    {
        private static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache()
        {
            Icons.Clear();Surfaces.Clear();Backdrops.Clear();
        }

        private enum Surface { Paper, Inset, Dark, Title, Gold, Green, Blue, Red,
            SmallGold, SmallGreen, SmallBlue, SmallRed, Frame, Badge, Stat, Count }
        private static readonly Dictionary<Surface,Texture2D> Surfaces=new Dictionary<Surface,Texture2D>();
        private static readonly Dictionary<string,Texture2D> Backdrops=new Dictionary<string,Texture2D>();

        public static void Apply(VisualElement root)
        {
            Paint(root,"menu-shell",Surface.Paper);
            Paint(root,"menu-title-row",Surface.Title);
            Paint(root,"deck-panel",Surface.Inset);
            Paint(root,"menu-catalog",Surface.Inset);
            Paint(root,"catalog-header",Surface.Title);
            Paint(root,"menu-tabs",Surface.Dark);
            Paint(root,"menu-button",Surface.Gold);
            Paint(root,"menu-primary",Surface.Green);
            Paint(root,"menu-friend-button",Surface.SmallGold);
            Paint(root,"menu-join",Surface.SmallGold);
            Paint(root,"settings-button",Surface.SmallGold);
            Paint(root,"menu-reset",Surface.SmallGold);
            Paint(root,"compact-button",Surface.SmallGold);
            Paint(root,"info-button",Surface.SmallBlue);
            Paint(root,"add-button",Surface.SmallGreen);
            Paint(root,"remove-button",Surface.SmallRed);
            Paint(root,"close-button",Surface.SmallRed);
            Paint(root,"modal-primary",Surface.SmallGreen);
            Paint(root,"menu-deck-count",Surface.Count);
            Paint(root,"menu-modal-panel",Surface.Paper);
            Paint(root,"card-action-panel",Surface.Dark);
            Paint(root,"detail-header",Surface.Title);
            Paint(root,"detail-cost",Surface.Badge);
            Paint(root,"dock",Surface.Dark);
            Paint(root,"timer",Surface.SmallGold);
            Paint(root,"hud-menu-button",Surface.SmallBlue);
            Paint(root,"hud-menu-panel",Surface.Paper);
            Paint(root,"small-button",Surface.SmallGold);
            Paint(root,"danger-button",Surface.SmallRed);
            Paint(root,"card-cost",Surface.Badge);
            Paint(root,"b3-card-frame",Surface.Frame);
            Paint(root,"lobby-panel",Surface.Paper);
            Paint(root,"primary-button",Surface.Green);
            Paint(root,"secondary-button",Surface.SmallGold);
            Paint(root,"waiting-leave",Surface.SmallRed);
            Attach(root.Q<Button>("settingsButton"),"settings",null);
            Attach(root.Q<Button>("hudMenuButton"),"more",null);
            Attach(root.Q<Button>("cardsTab"),"layers","卡牌");
            Attach(root.Q<Button>("battleTab"),"swords","對戰");
            Attach(root.Q<Button>("createButton"),"plus","建立房間");
            Attach(root.Q<Button>("joinButton"),"users","加入房間");
        }

        public static void SetMenuTab(Button tab,bool selected)
        {
            if(tab==null)return;
            tab.EnableInClassList("menu-tab-selected",selected);
            SetSurface(tab,selected?Surface.SmallGreen:Surface.SmallGold);
        }
        public static void SetChangeButton(Button button,bool remove)=>SetSurface(button,remove?Surface.SmallRed:Surface.SmallGreen);
        public static void PaintStat(VisualElement element)=>SetSurface(element,Surface.Stat);
        public static void PaintBadge(VisualElement element)=>SetSurface(element,Surface.Badge);
        public static void PaintCardFrame(VisualElement element)=>SetSurface(element,Surface.Frame);
        private static void Paint(VisualElement root,string className,Surface surface)
        {
            root.Query<VisualElement>(className:className).ForEach(e=>SetSurface(e,surface));
        }
        private static void SetSurface(VisualElement e,Surface surface)
        {
            if(e==null)return;
            foreach(var name in new[]{"skin-slice","skin-small-slice","skin-frame-slice","skin-badge"})
                e.RemoveFromClassList(name);
            e.AddToClassList(surface==Surface.Badge?"skin-badge":surface==Surface.Frame?"skin-frame-slice":
                surface==Surface.SmallGold||surface==Surface.SmallGreen||surface==Surface.SmallBlue||surface==Surface.SmallRed||
                surface==Surface.Stat||surface==Surface.Count?"skin-small-slice":"skin-slice");
            e.style.backgroundImage=Texture(surface);
        }
        private static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString("#"+hex,out Color color);return color;
        }
        private static Color Gradient(float t,Color top,Color middle,Color bottom)
        {
            return t<.5f?Color.Lerp(top,middle,t*2f):Color.Lerp(middle,bottom,(t-.5f)*2f);
        }
        private static bool Inside(float x,float y,float w,float h,float inset,float radius)
        {
            float left=inset,right=w-1-inset,bot=inset,top=h-1-inset;
            if(x<left||x>right||y<bot||y>top)return false;
            radius=Mathf.Max(1,radius);
            float dx=x-Mathf.Clamp(x,left+radius,right-radius);
            float dy=y-Mathf.Clamp(y,bot+radius,top-radius);
            return dx*dx+dy*dy<=radius*radius;
        }
        private static Texture2D Texture(Surface surface)
        {
            if(Surfaces.TryGetValue(surface,out var cached)&&cached)return cached;
            bool small=surface==Surface.SmallGold||surface==Surface.SmallGreen||surface==Surface.SmallBlue||
                surface==Surface.SmallRed||surface==Surface.Stat||surface==Surface.Count;
            bool panel=surface==Surface.Paper||surface==Surface.Inset||surface==Surface.Dark;
            bool badge=surface==Surface.Badge,frame=surface==Surface.Frame;
            int w=badge?96:panel?160:128,h=badge?96:panel?160:80;
            var tex=new Texture2D(w,h,TextureFormat.RGBA32,false){name="TowerDuel/FantasyB3/"+surface,
                filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
            var colors=new Color32[w*h];
            Color dark=C("241921"),brown=C("6b361e"),goldLight=C("fff4a6"),goldMid=C("e58f24"),goldDark=C("99501d");
            Color faceTop=C("fff9df"),faceMid=C("ffcd77"),faceBottom=C("dea052");
            switch(surface)
            {
                case Surface.Paper:faceTop=C("fff7df");faceMid=C("ffedc7");faceBottom=C("f3cd8f");break;
                case Surface.Inset:case Surface.Stat:faceTop=C("fff7df");faceMid=C("ffedc7");faceBottom=C("f0cc91");break;
                case Surface.Dark:case Surface.Count:faceTop=C("526387");faceMid=C("354565");faceBottom=C("172035");break;
                case Surface.Green:case Surface.SmallGreen:faceTop=C("edff68");faceMid=C("78c822");faceBottom=C("2c700e");break;
                case Surface.Blue:case Surface.SmallBlue:faceTop=C("90d8ff");faceMid=C("258ae0");faceBottom=C("19427f");break;
                case Surface.Red:case Surface.SmallRed:faceTop=C("ffc0a1");faceMid=C("f14d3b");faceBottom=C("8d2027");break;
                case Surface.Badge:faceTop=C("f5bfff");faceMid=C("d562f6");faceBottom=C("64159a");break;
            }
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                Color col=Color.clear;
                float t=1f-y/(float)(h-1);
                if(badge)
                {
                    float d=Vector2.Distance(new Vector2(x,y),new Vector2((w-1)*.5f,(h-1)*.5f));
                    if(d<=46)col=d>43?dark:d>39?Gradient(t,goldLight,goldMid,goldDark):d>36?brown:Gradient(t,faceTop,faceMid,faceBottom);
                }
                else
                {
                    float r=small?13f:panel?19f:18f;
                    float rim=small?7f:frame?9f:panel?13f:12f;
                    if(Inside(x,y,w,h,1,r))
                    {
                        if(!Inside(x,y,w,h,3,r-2))col=dark;
                        else if(!Inside(x,y,w,h,5,r-4))col=brown;
                        else if(!Inside(x,y,w,h,rim-1,r-5))col=Gradient(t,goldLight,goldMid,goldDark);
                        else if(!Inside(x,y,w,h,rim,r-6))col=goldLight;
                        else if(!Inside(x,y,w,h,rim+1,r-7))col=brown;
                        else if(!frame)
                        {
                            float ft=Mathf.Clamp01((h-rim-2-y)/Mathf.Max(1f,h-2*rim-4));
                            col=Gradient(ft,faceTop,faceMid,faceBottom);
                            if(!small&&!panel&&y>=h-rim-4)col=Color.Lerp(col,Color.white,.4f);
                        }
                        // Rivets appear only on large ornamental panels/title/primary buttons.
                        if(!small&&!frame&&surface!=Surface.Inset&&surface!=Surface.Dark)
                        {
                            float dx=Mathf.Min(Mathf.Abs(x-9),Mathf.Abs(x-(w-10)));
                            float dy=Mathf.Min(Mathf.Abs(y-9),Mathf.Abs(y-(h-10)));
                            float d=dx*dx+dy*dy;
                            if(d<=15)col=d>7?dark:Gradient(t,goldLight,goldMid,goldDark);
                        }
                    }
                }
                colors[y*w+x]=col;
            }
            tex.SetPixels32(colors);tex.Apply(false,true);Surfaces[surface]=tex;return tex;
        }

        // Character-specific, deliberately empty stages: no baked character,
        // captions or statistics. All characters are actual B3 idle atlas frames.
        public static Texture2D CharacterBackdrop(string id,bool spell)
        {
            if(Backdrops.TryGetValue(id,out var existing)&&existing)return existing;
            int seed=17;unchecked{foreach(char ch in id)seed=seed*31+ch;}
            var rng=new System.Random(seed);
            const int w=480,h=270;
            var pixels=new Color32[w*h];
            bool sky=id=="falcon"||id=="griffin",forest=id=="archer"||id=="swords",
                frost=id=="frost",fire=id=="fireball",arcane=spell;
            Color skyTop=frost?C("265c92"):fire?C("75333e"):sky?C("358dd6"):C("4c9cdb");
            Color skyBottom=frost?C("c4e9fa"):fire?C("ffb671"):C("c7e9ee");
            Color far=forest?C("729b65"):frost?C("94b7d2"):fire?C("8e5967"):C("7f9eb1");
            Color near=forest?C("4b7742"):frost?C("5685a9"):fire?C("693947"):C("567e8b");
            Color land=forest?C("a9c875"):frost?C("adc7dc"):fire?C("bf7b55"):C("b8bc81");
            Color stone=frost?C("dcebf3"):fire?C("a8674c"):C("d8c5a5");
            int cloudX=rng.Next(45,125),cloud2=rng.Next(315,430);
            float phase=(float)rng.NextDouble()*6f;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float z=y/(float)h;
                Color col=Color.Lerp(skyBottom,skyTop,z);
                bool cloud=Ellipse(x,y,cloudX,222,49,11)||Ellipse(x,y,cloudX-20,229,20,15)||
                    Ellipse(x,y,cloudX+9,234,23,19)||Ellipse(x,y,cloud2,208,47,12)||Ellipse(x,y,cloud2+12,218,22,17);
                if(cloud&&!arcane)col=Color.Lerp(col,C("fff8e7"),.83f);
                float ridge=122+24*Mathf.Sin(x*.021f+phase)+12*Mathf.Sin(x*.048f+1.1f);
                float front=98+14*Mathf.Sin(x*.019f+phase+2)+8*Mathf.Sin(x*.043f);
                if(y<ridge)col=Color.Lerp(far,skyBottom,.12f);
                if(y<front)col=near;
                if(!sky&&!arcane&&!forest&&y>80&&y<126)
                {
                    bool side=x<118||x>362;
                    int tx=x<118?x:x-362;
                    bool battlement=y<110||tx%28<18;
                    if(side&&battlement)
                    {
                        col=C("c7b69c");
                        if(y%16<2||(tx+(y/16%2)*17)%34<2)col=C("8b8177");
                        if(y>108)col=C("e7d3b3");
                    }
                }
                if(forest&&y>57&&y<165)
                {
                    for(int n=0;n<4;n++)
                    {
                        int cx=n<2?20+n*45:415+(n-2)*45;
                        if(Ellipse(x,y,cx,116+n%2*15,34,44)||Ellipse(x,y,cx-15,105,23,32))
                            col=Color.Lerp(near,land,.22f+((x+3*y)%13)*.008f);
                    }
                }
                if(y<67)col=land;
                // Courtyard/pedestal keeps the center readable behind the transparent idle sprite.
                float dx=(x-w*.5f)/172f,dy=(y-35f)/32f;
                if(dx*dx+dy*dy<=1f)
                {
                    col=stone;
                    if(dx*dx+dy*dy>.87f)col=Color.Lerp(stone,C("6e5c55"),.4f);
                    if(y<26)col=Color.Lerp(stone,C("806f64"),.26f);
                    if(y%16<1||(x+(y/16%2)*23)%47<1)col=Color.Lerp(col,C("8e8276"),.35f);
                }
                if(arcane)
                {
                    float dx2=x-w*.5f,dy2=y-133f;
                    float radius=Mathf.Sqrt(dx2*dx2+dy2*dy2);
                    if(Mathf.Abs(radius-100)<1.5f||Mathf.Abs(radius-122)<1f)
                        col=Color.Lerp(col,frost?C("b7f6ff"):C("ffe0a1"),.7f);
                    if((x*31+y*17+seed)%997==0)col=C("fff4c4");
                }
                float vignette=Mathf.Clamp01(Mathf.Abs((x-w*.5f)/(w*.5f))*.30f);
                col=Color.Lerp(col,C("26334b"),vignette);
                pixels[y*w+x]=col;
            }
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false){name="TowerDuel/CharacterStage/"+id,
                filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
            texture.SetPixels32(pixels);texture.Apply(false,true);Backdrops[id]=texture;return texture;
        }
        private static void Attach(Button button, string key, string text)
        {
            if (button == null) return;
            button.text = "";
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("menu-icon");
            icon.style.backgroundImage = Get(key);
            button.Add(icon);
            if (text != null)
            {
                var label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("icon-label");
                button.Add(label);
                button.tooltip = text;
            }
        }

        private static Texture2D Get(string key)
        {
            if (Icons.TryGetValue(key, out var cached) && cached) return cached;
            if (key == "plus" || key == "users")
            {
                var icon = RoomIcon(key);
                Icons[key] = icon;
                return icon;
            }
            string png = key == "settings" ? Settings : key == "more" ? More : key == "layers" ? Layers : Swords;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "TowerDuel/ModernB3/" + key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.LoadImage(Convert.FromBase64String(png), true);
            Icons[key] = texture;
            return texture;
        }

        // Alpha-only silhouettes so USS can tint icons without baked colors.
        private static Texture2D RoomIcon(string key)
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int covered = 0;
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                {
                    float px = (x + (sx + .5f) / 3f) / size;
                    float py = (y + (sy + .5f) / 3f) / size;
                    bool ink = key == "plus"
                        ? Capsule(px, py, .5f, .21f, .5f, .79f, .075f) ||
                          Capsule(px, py, .21f, .5f, .79f, .5f, .075f)
                        : Circle(px, py, .37f, .72f, .15f) ||
                          Circle(px, py, .73f, .66f, .115f) ||
                          (py >= .16f && py <= .48f && Ellipse(px, py, .37f, .26f, .27f, .24f)) ||
                          (py >= .16f && py <= .40f && Ellipse(px, py, .75f, .23f, .19f, .18f));
                    if (ink) covered++;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(covered * 255 / 9));
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "TowerDuel/ModernB3/" + key, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static bool Circle(float x, float y, float cx, float cy, float r) =>
            (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
        private static bool Ellipse(float x, float y, float cx, float cy, float rx, float ry) =>
            Mathf.Pow((x - cx) / rx, 2f) + Mathf.Pow((y - cy) / ry, 2f) <= 1f;
        private static bool Capsule(float x, float y, float ax, float ay, float bx, float by, float radius)
        {
            var a = new Vector2(ax, ay);
            var b = new Vector2(bx, by);
            var p = new Vector2(x, y);
            var d = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
            return (p - a - t * d).sqrMagnitude <= radius * radius;
        }

        private const string Settings = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAoBAMAAAB+0KVeAAAAMFBMVEUAAAD///////////////////////////////8AAAAAAAAAAAAAAAAAAAAAAAAAAABOBIVFAAAAEHRSTlMABPu0cCpSz5QAAAAAAAAA5PfpHAAAAfhJREFUeNqVkr1PFEEYh5937/YkEm7eMaAxhg/3EC1MgEvoTPgoaOEOOgohamJh6T9AYW9na2EvnaVXmNgdoaAiC/gRjBiZWUGFqIzFbk4LY/RXTZ7MZGbe5wd/iHQWCo6/pLOzMsWlx+F3aAnNFvG+VwcQAYhzMgTfpozTDjS9K84v1DHZbAZQAuTk2nbj2cQjO3Fme9Qf51C7Od1Bdy62PYfv8uMig7JMc01GiIdOJzV/QFcyHs8qsLhSSa4ICEg0eIAXjDg0OdoKRECJhlg1GdZ+9mcNRKDdrAfvMuOCL3EZKIMTC6Zcu9FuASGDCPpmGZNs4MPT10Z/MDwHIrqyFu8TGcDshsRHaYg4WourPoxSq3PoZWQ+ABInq6D1YZipCVyoCRHw3Eo2JdZq2aj1xZTklwZxWsC9B5hWcM5/z8KtJYrb5SDUd2umHaW9PURpiEJ4cidMygZpmx495Nz1gCAh7q++8uMe+cjMRgpEBKsQzNu+xkAG2GKepWTZWkSxtitZzqE2a6JYFYvEyZgWNsPNL9POq1s0JQqbJ2n1Tf/7++th6WX3vc09jhFAmy2I98s9wNzDkCs+2axWZra+fjK3B9IXXcfFj/XufCWZXrjK+VXpFEyCqAU5cKjPHUGwAfWUvM17WwbAoYmnf8v9S5MtBM//5ideXKw2f5UitQAAAABJRU5ErkJggg==";
        private const string More = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAoBAMAAAB+0KVeAAAAMFBMVEUAAAD///////////////////////////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABEUdjSAAAAEHRSTlMAA/klUHWvyQAAAAAAAAAAmXZOUQAAAHdJREFUeNrtzqEOgmAAxPH/8VEMbOdmJ1q/R9Bo8hF8ZSm+gwk2psPAPgJzBoGglV+4dNsdrP6hLSC/Y2QMFgh9mlEQIjgYIAN8fNo+PyRddgICsDnd9ve2S+WrrfO+G5tfMiBd8yoVMVRNceibuaGFS544v/rFAKJjFwy3I827AAAAAElFTkSuQmCC";
        private const string Layers = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAoBAMAAAB+0KVeAAAAMFBMVEUAAAD///////////////////////////////8AAAAAAAAAAAAAAAAAAAAAAAAAAABOBIVFAAAAEHRSTlMA/AbQby5PrpIAAAAAAAAA4nsEjwAAAU9JREFUeNqtUrFOwzAUPF6btAPL4UrphB5WQKyWoq60yQ9ELGUjoj9Ahs7QSt35NL4Aie7AxobE4BgaGsHCW2yfbL+7ewf8U4l2YR2oikrY95rVPs960dFb657FgpzAtrE5SS53UYs5OZq1UItzcqyyItdoKAhSMilFo5rMG1TPHJNKVDSqaW4EAOTU0VSigGjsaC4VgAzIqVcj2idvFQJ8AO/HAgC6vQYe/Z+Bt1fgxYoU5BLWYk5mGjh5hp6tAjgA7MnF/d0D3PYFg+Hw8PUJgAi5jmoax6RMSf/ermimsSNN1XccN+pj58/T2NFUwfTUMSnTPKpp8mC/IA0uBT+gRY4r7+cGaeYNEZo8OJ+60L2mqfyMYsfE7vSRItNWp5Qcl6LRisy/woErcqQyIzfQ1jQni70ZL8j9NEhBZmp/5ms2+k5YV+p+z2dnkv+sT7V6PMWb01NnAAAAAElFTkSuQmCC";
        private const string Swords = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAoBAMAAAB+0KVeAAAAMFBMVEUAAAD///////////////////////////////8AAAAAAAAAAAAAAAAAAAAAAAAAAABOBIVFAAAAEHRSTlMA/AOtkHAzSs4AAAAAAAAADveiJwAAAcVJREFUeNqtk89qE1EUxn9zZtIiDUnGRtFSk9PbTRZqqgtLQUO6MStDX6AL/zyB+hS+hK8gfYZ24UZKqbhLhpGQoGEyHcRY09xxkaSj4LJ3dfn4znfPD74LV3B0cZFMM2IUQIyav52iIDp3uoC5nrR/hCnV8O3PtWsjcADd/YA8OeHr3YDc0GbJ9jCwjwNYV8ADYHnr47RxeMab5N3lM89X5aVf3vUfKSZbLKfvdXos30LTycQJmkBdbAcuM3MBFLH/cp23K5Ump4FmPLLp3xAjD/zmHNIFnLgWtfrFyXjYr8aLcVtJOAol1WkjYyeJa1Gr7/xeWF2AtDoZ9wZx4fZoOxktRJLSrag1loVVAKPYEkch061FKiDG831/zyy98ssyG9eb+bQ+gF5//6De30ljPJCV/MkZcNEuTYXP38FBKMr9Y3j4yYsKy+e5ocUlre50ByBfatFTv8d+P8YBcfPARuRGXj6WkQUXnI0xCE5Qi36RbhMjUHkG3KFTSUA4FRCQGJCAsKvc06lVHBBvBaRoA3Ujb1QqdBCwa3tu0zYg7OrFer4zp1x6nVs1CrpZNpL1S2etkxeStVJlVlH9X21Fr+IL/AEdnJtqrUA/9gAAAABJRU5ErkJggg==";
    }
}
