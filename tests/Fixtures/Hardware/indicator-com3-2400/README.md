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
- **Active Production Profile:** Locked to `DecimalPlaces = 1`, `WeightDigits = 7`, `ScaleFactor = 1.0` following empirical correlation across 0–145 kg (`0000150` $\rightarrow$ `15.0 kg`, `0000900` $\rightarrow$ `90.0 kg`, `0001450` $\rightarrow$ `145.0 kg`).
