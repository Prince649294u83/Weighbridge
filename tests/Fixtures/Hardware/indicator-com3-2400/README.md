# Physical Hardware Fixture: Indicator COM3 @ 2400 Baud

## Overview
This fixture directory preserves the empirical ground truth for the physical weighing indicator connected via the Megawin MA112 USB-to-UART bridge on `COM3` @ `2400` baud (8N1, DTR=True, RTS=True).

## Protocol Specification
- **Frame Header:** `[` (`0x5B`)
- **Payload:** 7 ASCII numeric characters (`0`-`9`)
- **Frame Terminator:** `\0` (`0x00`)
- **Total Packet Length:** 9 bytes

## Decoder Engine vs. Active Profile
- **Decoder Engine (`WeightDecoder`):** Pure deterministic string transformation supporting `WeightDigits`, `DecimalPlaces`, `DigitsToRemoveFromEnd`, `ReversePayload`, `DummyZero`, `ScaleFactor`.
- **Active Production Profile:** Gated on empirical multi-point correlation table (`correlation.json`). Default profile preserves integer baseline (`0001450` $\rightarrow$ `1450.0 kg`).
