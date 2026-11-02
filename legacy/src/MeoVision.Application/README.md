# MeoVision.Application (net48)

Recipe/cycle orchestration/alarm/logging: `InspectionCycleService` (retry theo `VisionFailureReason`),
`IRecipeRepository`/`JsonFileRecipeRepository`, `IInspectionResultStore`, `IAlarmService`,
`INgImageArchiver`, `ServiceCollectionExtensions` (DI). Chỉ phụ thuộc `IVisionEngine`/`ICameraSource`/
`IPlcCommunication` (`Core`) — không quan tâm là bản Simulated hay Engine thật.
