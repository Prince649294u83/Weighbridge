# WeighBridge Project Context

## Repository
E:\Projects\WeighBridge Project

## Application
.NET 8 WPF Windows desktop application.

## Important projects
- WeighBridge.App
- WeighBridge.Core
- WeighBridge.Domain
- WeighBridge.Hardware
- WeighBridge.Infrastructure
- WeighBridge.Printing
- WeighBridge.Reporting
- WeighBridge.Services
- WeighBridge.Settings
- WeighBridge.Tests

## Primary workflow
Vehicle Entry / Weighment.

## Desktop automation
Use Windows UI Automation and FlaUI.

## Hardware
Prompt 21 hardware layer must be preserved unless there is explicit evidence requiring change.

## Evidence rule
Never claim that a desktop interaction succeeded based solely on a command that launched a process. Verify the visible application state and, where applicable, the persisted database state.

## Client constraints
- Small local deployment.
- Current client does not use RFID.
- Camera belongs to automatic workflow, not manual workflow.
- Recent Pages section is not required.
