using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel
{
    // Shared, presentation-only ground sprite atlas. The authored outer band
    // has a radius of 150 pixels, so at 150 PPU it marks a one-unit radius.
    // Its decorative tips and soft glow may extend beyond the band.
    public sealed class DuelAuraArt : IDisposable
    {
        public const int TileSize=384, FrameCount=9;
        public const float PixelsPerRadius=150f;
        public const double TicksPerFrame=5d, TicksPerRevolution=360d;
        private readonly Sprite[] frames=new Sprite[FrameCount];
        private readonly Material material;
        public bool Available=>material&&frames[0];

        public DuelAuraArt()
        {
            var atlas=Resources.Load<Texture2D>("TowerDuel/B3/standard-aura");
            var shader=Resources.Load<Shader>("TowerDuel/B3/AuraGround");
            if(!atlas||atlas.width!=TileSize*3||atlas.height!=TileSize*3||!shader||!shader.isSupported)
            {
                Debug.LogWarning("Missing or invalid standard aura atlas/shader; using the radius line fallback.");
                return;
            }
            material=new Material(shader){name="Standard aura ground"};
            for(int i=0;i<FrameCount;i++)
            {
                int column=i%3,row=i/3;
                var rect=new Rect(column*TileSize,(2-row)*TileSize,TileSize,TileSize);
                frames[i]=Sprite.Create(atlas,rect,new Vector2(.5f,.5f),PixelsPerRadius,0,SpriteMeshType.FullRect);
                frames[i].name="Standard aura "+i;
            }
        }

        public static int FrameAt(double elapsedTicks)
        {
            return (int)(Math.Floor(Math.Max(0,elapsedTicks)/TicksPerFrame)%FrameCount);
        }

        public static Quaternion RotationAt(double elapsedTicks)
        {
            // Positive yaw turns +Z toward +X: clockwise from above.
            float yaw=(float)(Math.Max(0,elapsedTicks)%TicksPerRevolution*360d/TicksPerRevolution);
            return Quaternion.Euler(0,yaw,0)*Quaternion.Euler(90,0,0);
        }

        public SpriteRenderer Create(Transform parent)
        {
            if(!Available)throw new InvalidOperationException("Aura artwork unavailable");
            var sprite=new GameObject("Standard aura sprite").AddComponent<SpriteRenderer>();
            sprite.transform.SetParent(parent,false);
            sprite.sharedMaterial=material;
            sprite.sprite=frames[0];
            sprite.shadowCastingMode=ShadowCastingMode.Off;
            sprite.receiveShadows=false;
            sprite.enabled=false;
            return sprite;
        }

        public void Present(SpriteRenderer sprite,Vector3 centre,float radius,double elapsedTicks)
        {
            sprite.sprite=frames[FrameAt(elapsedTicks)];
            sprite.transform.SetPositionAndRotation(
                new Vector3(centre.x,DuelMap.GroundHeight(centre.z)+.025f,centre.z),
                RotationAt(elapsedTicks));
            // The artwork is independent of the character's facing and scale.
            // Never pulse scale: only the authored light changes between frames.
            sprite.transform.localScale=new Vector3(radius,radius,1);
        }

        public void Dispose()
        {
            foreach(var frame in frames)if(frame)UnityEngine.Object.Destroy(frame);
            if(material)UnityEngine.Object.Destroy(material);
        }
    }
}
