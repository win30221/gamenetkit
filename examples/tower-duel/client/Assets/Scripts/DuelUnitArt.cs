using UnityEngine;

namespace TowerDuel
{
    public sealed class DuelUnitArt : MonoBehaviour
    {
        public Animator Animator;
        public Transform WalkProbe, AttackProbe, DeathProbe;
        public Renderer TeamMarker;
        public float Height;
    }
}
