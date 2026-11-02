# MeoVision.Core (net48)

Contracts/POCO trung lập, không có implementation: `IVisionEngine`, `ICameraSource`,
`IPlcCommunication`, `VisionResult`, `VisionRecipe`, `VisionJobRequest`, `VisionCheckResult`,
`VisionTrainRegion`, `CameraTriggerMode`, `VisionEngineException` (+3 lớp con), `Guard`.

KHÔNG project nào khác được `using Cognex.VisionPro.*` trừ `MeoVision.Engine` (ranh giới
TRONG PROCESS — xem `docs/design-notes/DESIGN_LOG.md` mục [2]).
