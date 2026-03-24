using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class EmptyGraph : Graphic
    {
        public bool Debug = false;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

#if UNITY_EDITOR

            if (Debug)
            {
                base.OnPopulateMesh(vh);
            }

#endif
        }
    }
}