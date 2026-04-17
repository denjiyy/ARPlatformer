using UnityEngine;

namespace ARPlatformer
{
    public static class PlatformerCharacterControllerDefaults
    {
        public static readonly Vector3 Center = new(0f, 0.9f, 0f);

        public const float Height = 1.8f;
        public const float Radius = 0.24f;
        public const float SlopeLimit = 55f;
        public const float StepOffset = 0.2f;
        public const float SkinWidth = 0.02f;
        public const float MinMoveDistance = 0f;

        public static bool Apply(CharacterController characterController)
        {
            if (characterController == null)
                return false;

            var changed = false;

            if (characterController.center != Center)
            {
                characterController.center = Center;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.height, Height))
            {
                characterController.height = Height;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.radius, Radius))
            {
                characterController.radius = Radius;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.slopeLimit, SlopeLimit))
            {
                characterController.slopeLimit = SlopeLimit;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.stepOffset, StepOffset))
            {
                characterController.stepOffset = StepOffset;
                changed = true;
            }

            if (!Mathf.Approximately(characterController.skinWidth, SkinWidth))
            {
                characterController.skinWidth = SkinWidth;
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
