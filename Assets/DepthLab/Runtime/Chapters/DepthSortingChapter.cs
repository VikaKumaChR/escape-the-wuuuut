using UnityEngine;
using DistortedSpace.Localization;

namespace DepthLab
{
    public sealed class DepthSortingChapter : LabChapter
    {
        public ChapterPiece[] pieces;
        public Transform[] slots;
        public Renderer[] slotIndicators;
        public Material filledSlotMaterial;
        public Material[] emptySlotMaterials;
        public float snapRadius = .58f;
        public Vector3 boundsHalfSize = new Vector3(6f, 4f, 7f);
        private int placed;
        private string feedback;

        public override string Objective => Loc.T("1. 抓取蓝色立方体、紫色球体和青色三棱柱实体。\n2. 放入各自对应的线框目标。\n3. 全部匹配后，步行穿过已开启的出口。");
        public override string Hint => PuzzleSolved ? Loc.T("3. 三件实体均已匹配。请步行穿过已开启的出口。") :
            string.IsNullOrEmpty(feedback) ? Loc.T("1. 抓彩色实体，不抓线框：桌面按住鼠标左键，滚轮调远近；启用 XR 后可用 VR 控制器抓取、借助双眼视差判断深度。\n2. 在对应线框中心附近松手。线框是无碰撞的目标影像；彩色物体与底座是有碰撞的实体。") : Loc.T(feedback);
        public override int Progress => placed;
        public override int Goal => 3;
        protected override bool PuzzleSolved => placed == Goal;

        public override void Begin()
        {
            placed = 0;
            feedback = "";
            ResetExit();
            if (pieces != null)
                foreach (ChapterPiece piece in pieces)
                    if (piece != null) piece.ResetPiece();
            if (slotIndicators != null && emptySlotMaterials != null)
                for (int i = 0; i < slotIndicators.Length && i < emptySlotMaterials.Length; i++)
                    if (slotIndicators[i] != null) slotIndicators[i].sharedMaterial = emptySlotMaterials[i];
            RefreshStatus();
        }

        public void PieceReleased(ChapterPiece piece)
        {
            if (!InteractionAllowed || piece == null || piece.IsPlaced || piece.chapter != this) return;
            int nearest = -1;
            float distance = snapRadius * snapRadius;
            for (int i = 0; slots != null && i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                float d = (piece.transform.position - slots[i].position).sqrMagnitude;
                if (d <= distance) { nearest = i; distance = d; }
            }
            if (nearest >= 0 && nearest == piece.identity)
            {
                piece.SnapTo(slots[nearest]);
                placed++;
                if (slotIndicators != null && nearest < slotIndicators.Length && slotIndicators[nearest] != null)
                    slotIndicators[nearest].sharedMaterial = filledSlotMaterial;
                feedback = "2. 匹配成功！继续将剩余彩色实体放入对应线框。各目标的实际深度不同。";
                DepthLabApp.Instance.ReportTutorialAction("place");
            }
            else
            {
                Mistake();
                feedback = nearest >= 0
                    ? "形状不匹配。物体留在原处，请放进同形状的线框。"
                    : "尚未对准目标。物体会自然落下，可以在落点重新抓取。";
            }
            RefreshStatus();
        }

        public bool IsOutOfBounds(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) > boundsHalfSize.x || Mathf.Abs(local.z) > boundsHalfSize.z ||
                local.y < -.5f || local.y > boundsHalfSize.y;
        }

        public void ResetOutOfBounds(ChapterPiece piece)
        {
            if (!InteractionAllowed || piece == null || piece.IsPlaced) return;
            Mistake();
            piece.ResetPiece();
            feedback = "物体掉出地图，已回收到起始台。普通松手不会传送物体。";
            RefreshStatus();
        }
    }
}
