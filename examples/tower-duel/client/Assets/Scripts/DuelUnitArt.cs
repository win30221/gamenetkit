using UnityEngine;

namespace TowerDuel
{
    public sealed class DuelUnitArt : MonoBehaviour
    {
        public Animator Animator;
        public DuelSpriteUnit Sprites;
        public Transform WalkProbe, AttackProbe, DeathProbe;
        public Renderer TeamMarker;
        public float Height;
    }
}
