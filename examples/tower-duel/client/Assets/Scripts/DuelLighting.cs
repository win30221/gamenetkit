using UnityEngine;

namespace TowerDuel
{
    public static class DuelLighting
    {
        private static Light sun;
        public static Light Sun => sun;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => sun = null;

        public static Light Apply(Camera view)
        {
            if (!view) return null;
            if (!sun || sun.gameObject.scene != view.gameObject.scene)
            {
                sun = null;
                foreach (var candidate in Object.FindObjectsByType<Light>())
                    if (candidate.type == LightType.Directional && candidate.enabled && candidate.gameObject.scene == view.gameObject.scene)
                    { sun = candidate; break; }
            }
            if (!sun) return null;
            var forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
            float verticalProjection = Mathf.Max(.2f, Vector3.Dot(forward, view.transform.up));
            // A local presentation choice: both seats see upper-right light and
            // lower-left cast shadows, independently of the authoritative world axes.
            var shadow = (-right-forward/verticalProjection).normalized;
            sun.transform.rotation = Quaternion.LookRotation(shadow+Vector3.down*1.5f, Vector3.up);
            return sun;
        }
        public static Vector2 ScreenShadow(Camera view)
        {
            if(!view||!sun)return Vector2.zero;
            var ray=sun.transform.forward;ray.y=0;
            var origin=view.WorldToScreenPoint(Vector3.zero);
            var end=view.WorldToScreenPoint(ray);
            return new Vector2(end.x-origin.x,end.y-origin.y).normalized;
        }
    }
}
