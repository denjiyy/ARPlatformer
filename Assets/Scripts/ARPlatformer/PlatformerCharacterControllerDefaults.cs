using UnityEngine;

namespace ARPlatformer
{
    public static class PlatformerCharacterControllerDefaults
    {
        public static readonly Vector3 Center = new(0f, 0.9f, 0f);

        public const float Height = 1.8f;
        public const float Radius = 0.24f;
        public const float SlopeLimit = 55f;
        public const float StepOffset = 0.1f;
        public const float SkinWidth = 0.08f;
        public const float MinMoveDistance = 0f;

        public static bool Apply(CharacterController characterController)
        {
            return ApplyScaled(characterController, 1f);
        }

        public static bool ApplyScaled(CharacterController characterController, float scale)
        {
            if (characterController == null)
                return false;

            var changed = false;

            var scaledCenter = Center * scale;
            if (characterController.center != scaledCenter)
            {
                characterController.center = scaledCenter;
                changed = true;
            }

            var scaledHeight = Height * scale;
            if (!Mathf.Approximately(characterController.height, scaledHeight))
            {
                characterController.height = scaledHeight;
                changed = true;
            }

            var scaledRadius = Radius * scale;
            if (!Mathf.Approximately(characterController.radius, scaledRadius))
            {
                characterController.radius = scaledRadius;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.slopeLimit, SlopeLimit))
            {
                characterController.slopeLimit = SlopeLimit;
                changed = true;
            }

            var scaledStepOffset = StepOffset * scale;
            if (!Mathf.Approximately(characterController.stepOffset, scaledStepOffset))
            {
                characterController.stepOffset = scaledStepOffset;
                changed = true;
            }

            var scaledSkinWidth = SkinWidth * scale;
            if (!Mathf.Approximately(characterController.skinWidth, scaledSkinWidth))
            {
                characterController.skinWidth = scaledSkinWidth;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.minMoveDistance, MinMoveDistance))
            {
                characterController.minMoveDistance = MinMoveDistance;
                changed = true;
            }

            return changed;
        }
    }
}
