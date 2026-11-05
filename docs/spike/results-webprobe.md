# Rhino-free WebView2 probe

Measured 2026-11-05T18:56:38.8274484+08:00. Core API package 1.0.1938.49; .NET 8.0.33. This probe does not include Rhino, GPU upload or DisplayBitmap.


| Host | Scale | Six cards p50 / p95 ms | Single 240×180 hover p50 / p95 ms | Alpha corner / center |
|---|---:|---:|---:|---|
| Offscreen | 1 | 388.03 / 423.06 | 92.89 / 95.44 | 0 / 255; 280×92 |
| Offscreen | 1.5 | 402.54 / 425.03 | 93.05 / 94.57 | 0 / 255; 420×138 |
| Offscreen | 2 | 420.98 / 464.39 | 93.88 / 109.24 | 0 / 255; 560×184 |
| Offscreen | 4 | 498.47 / 530.05 | 107.66 / 110.64 | 0 / 255; 1120×368 |
| Hidden | 1 | 401.85 / 422.32 | 93.72 / 95.18 | 0 / 255; 280×92 |
| Hidden | 1.5 | 408.79 / 440.87 | 93.39 / 108.99 | 0 / 255; 420×138 |
| Hidden | 2 | 421.81 / 471.08 | 93.02 / 108.64 | 0 / 255; 560×184 |
| Hidden | 4 | 501.53 / 528.16 | 93.95 / 124.98 | 0 / 255; 1120×368 |
| Minimized | 1 | 400.37 / 422.15 | 93.03 / 94.79 | 0 / 255; 280×92 |
| Minimized | 1.5 | 420.39 / 438.88 | 93.64 / 95.41 | 0 / 255; 420×138 |
| Minimized | 2 | 421.01 / 453.74 | 93.81 / 124.91 | 0 / 255; 560×184 |
| Minimized | 4 | 512.57 / 534.11 | 94.60 / 110.66 | 0 / 255; 1120×368 |

Offscreen browser kill/re-init/resync: 689.93 ms, 6 cards recovered. Runtime 156.0.4314.58.
Hidden browser kill/re-init/resync: 653.29 ms, 6 cards recovered. Runtime 156.0.4314.58.
Minimized browser kill/re-init/resync: 659.00 ms, 6 cards recovered. Runtime 156.0.4314.58.
