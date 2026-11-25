using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDuel
{
    /// <summary>
    /// UI-only idle presentation. Play the authored idle loop when available.
    /// Legacy single-frame ground units retain local deformation; legacy
    /// flyers reuse the authored flight loop. No battle state is changed.
    /// </summary>
    internal sealed class DuelCharacterIdlePreview : System.IDisposable
    {
        private readonly Image view;
        private readonly VisualElement shadow;
        private DuelSpriteSet set;
        private readonly DuelUnitAssets assets;
        private string pendingId;
        private bool pendingFlight;
        private RenderTexture target;
        private Material material;
        private bool flying, active;
        private float started, nextFrame;

        public DuelCharacterIdlePreview(Image view, VisualElement shadow,DuelUnitAssets assets)
        {
            this.view=view;this.shadow=shadow;this.assets=assets;
            view.scaleMode=ScaleMode.ScaleToFit;
        }

        public void Show(string id,bool flight)
        {
            Stop();
            set=Resources.Load<DuelSpriteSet>("TowerDuel/B3/"+id);
            if(!set)
            {
                ShowSpell(DuelSpriteLibrary.Card(id));
                return;
            }
            if(!set.HasAtlases)
            {
                ShowSpell(DuelSpriteLibrary.Card(id));
                pendingId=id;pendingFlight=flight;
                assets.Prioritize(id);
                return;
            }
            flying=flight;
            shadow.style.display=DisplayStyle.Flex;
            shadow.style.opacity=flying ? .55f : 1f;
            var shader=Resources.Load<Shader>("TowerDuel/CharacterIdlePreview");
            if(shader&&shader.isSupported)
            {
                material=new Material(shader){hideFlags=HideFlags.DontSave};
                // Preserve fine sprite linework before UI Toolkit resamples the preview.
                target=new RenderTexture(512,512,0,RenderTextureFormat.ARGB32)
                {
                    name="TowerDuel/CharacterPreview", filterMode=FilterMode.Bilinear,
                    wrapMode=TextureWrapMode.Clamp, useMipMap=false, autoGenerateMips=false,
                    hideFlags=HideFlags.DontSave
                };
                if(target.Create())view.image=target;
                else ReleaseRenderer();
            }
            active=true;started=Time.unscaledTime;nextFrame=started;
            Tick();
        }

        public void ShowSpell(Texture2D illustration)
        {
            Stop();
            view.image=illustration;
            shadow.style.display=DisplayStyle.None;
        }

        public void Tick()
        {
            if(pendingId!=null && assets.IsLoaded(pendingId))
            {
                string id=pendingId;bool flight=pendingFlight;
                Show(id,flight);
            }
            if(!active||!set)return;
            float now=Time.unscaledTime;
            if(now<nextFrame)return;
            bool authoredIdle=set.FrameCount(DuelSpriteClip.Idle)>1;
            var clip=flying&&!authoredIdle?DuelSpriteClip.Walk:DuelSpriteClip.Idle;
            nextFrame=now+1f/Mathf.Max(30f,set.LoopFps(clip));
            float elapsed=now-started;
            int frame=set.LoopFrame(clip,elapsed);
            var sprite=set.Frame(clip,0,frame);
            if(!sprite)return;
            if(!material||!target)
            {
                // Graphics fallback: retain actual flight frames, or show the
                // authored idle pose with a tiny foot-anchored UI deformation.
                view.sprite=sprite;
                float breath=flying||authoredIdle?0f:Mathf.Sin(elapsed*3.4f);
                view.style.scale=new Scale(new Vector3(1+.012f*breath,1+.018f*breath,1));
                view.style.transformOrigin=new TransformOrigin(Length.Percent(50),Length.Percent(79),0);
                return;
            }
            var rect=sprite.rect;
            material.SetVector("_FrameRect",new Vector4(rect.x/sprite.texture.width,rect.y/sprite.texture.height,
                rect.width/sprite.texture.width,rect.height/sprite.texture.height));
            material.SetVector("_Pose",new Vector4(elapsed,1f-set.Baseline/(float)set.TileSize,
                set.ReferenceHeight/(float)set.TileSize,flying?1f:0f));
            material.SetFloat("_AuthoredIdle",authoredIdle?1f:0f);
            Graphics.Blit(sprite.texture,target,material);
            view.MarkDirtyRepaint();
        }

        public void Stop()
        {
            active=false;set=null;pendingId=null;
            view.sprite=null;view.image=null;
            view.style.scale=new Scale(Vector3.one);
            shadow.style.display=DisplayStyle.None;
            ReleaseRenderer();
        }

        private void ReleaseRenderer()
        {
            if(target){target.Release();Object.Destroy(target);target=null;}
            if(material){Object.Destroy(material);material=null;}
        }
        public void Dispose()=>Stop();
    }
}
