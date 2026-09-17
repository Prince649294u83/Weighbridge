import json
import os
import sqlite3
import subprocess
import time
import win32file
import win32pipe
import pywintypes
try:
    from mcp.server.mcpserver import MCPServer as FastMCP
except (ImportError, ModuleNotFoundError):
    from mcp.server.fastmcp import FastMCP

mcp = FastMCP("smart-weighbridge")

PIPE_NAME = "WeighBridgeDesktopAutomationPipe"
PIPE_PATH = r"\\.\pipe\WeighBridgeDesktopAutomationPipe"
HOST_EXE_PATH = r"E:\Projects\WeighBridge Project\tools\mcp\smart-weighbridge-mcp\bin\Debug\net8.0-windows\DesktopAutomationHost.exe"
DB_PATH = os.path.expandvars(r"%LOCALAPPDATA%\WeighBridge Modern\Data\weighbridge.db")

def is_pipe_available() -> bool:
    try:
        win32pipe.WaitNamedPipe(PIPE_PATH, 100)
        return True
    except pywintypes.error as e:
        return e.winerror != 2
    except Exception:
        return False

def ensure_automation_host():
    """Ensure the Desktop Automation IPC Service Host is running in Session 1."""
    if not is_pipe_available():
        if os.path.exists(HOST_EXE_PATH):
            subprocess.Popen([HOST_EXE_PATH], creationflags=subprocess.CREATE_NEW_CONSOLE)
            for _ in range(15):
                time.sleep(1)
                if is_pipe_available():
                    break

def send_ipc_request(payload: dict) -> dict:
    """Send JSON request over NamedPipe to DesktopAutomationHost service using win32file."""
    ensure_automation_host()
    if not is_pipe_available():
        return {
            "Success": False,
            "Status": "HOST_UNAVAILABLE",
            "Message": "NamedPipe host unavailable or interactive session inaccessible."
        }

    handle = None
    try:
        handle = win32file.CreateFile(
            PIPE_PATH,
            win32file.GENERIC_READ | win32file.GENERIC_WRITE,
            0,
            None,
            win32file.OPEN_EXISTING,
            0,
            None
        )

        message = (json.dumps(payload) + "\n").encode("utf-8")
        win32file.WriteFile(handle, message)

        chunks = []
        while True:
            hr, data = win32file.ReadFile(handle, 4096)
            chunks.append(data)
            if b"\n" in data or len(data) == 0:
                break

        full_response = b"".join(chunks).decode("utf-8-sig").strip()
        if full_response:
            return json.loads(full_response)
        return {"Success": False, "Status": "EMPTY_RESPONSE", "Message": "Empty response received from host."}
    except pywintypes.error as e:
        return {"Success": False, "Status": "PIPE_ERROR", "Message": f"Windows NamedPipe error: {e.strerror} (Code {e.winerror})"}
    except Exception as ex:
        return {"Success": False, "Status": "EXCEPTION", "Message": f"IPC Communication Error: {str(ex)}"}
    finally:
        if handle is not None:
            win32file.CloseHandle(handle)

def get_test_credentials():
    """Resolve test credentials with strict priority: env vars -> repository test fixture."""
    username = os.environ.get("WEIGHBRIDGE_TEST_USERNAME") or "admin"
    password = os.environ.get("WEIGHBRIDGE_TEST_PASSWORD") or "admin123"
    return username, password

@mcp.tool()
def project_status() -> str:
    """Return verified information about the Smart Weighbridge project."""
    username, password = get_test_credentials()
    cred_status = "TEST CREDENTIALS AVAILABLE" if (username and password) else "TEST CREDENTIALS REQUIRED"
    return (
        "Smart Weighbridge MCP is active.\n"
        "Project Root: E:\\Projects\\WeighBridge Project\n"
        "Application: .NET 8 WPF\n"
        "Database: SQLite (EF Core)\n"
        "Automation: Windows UI Automation / FlaUI IPC Bridge\n"
        f"Credential Status: {cred_status}"
    )

@mcp.tool()
def git_status() -> str:
    """Return the git status of the repository."""
    result = subprocess.run(["git", "status", "-s"], capture_output=True, text=True, cwd=r"E:\Projects\WeighBridge Project")
    return result.stdout if result.returncode == 0 else result.stderr

@mcp.tool()
def build() -> str:
    """Build the WeighBridge .NET 8 solution."""
    result = subprocess.run(["dotnet", "build", "WeighBridge.sln"], capture_output=True, text=True, cwd=r"E:\Projects\WeighBridge Project")
    return result.stdout if result.returncode == 0 else result.stderr

@mcp.tool()
def launch_app(app_type: str = "weighbridge") -> str:
    """Launch WeighBridge WPF application with simulator enabled via Desktop Automation Host."""
    if app_type.lower() == "weighbridge":
        exe_path = r"E:\Projects\WeighBridge Project\src\WeighBridge.App\bin\Debug\net8.0-windows\win-x64\WeighBridge.App.exe"
        title = "Smart Weighbridge"
        shot_name = "weighbridge_live_proof.png"
    else:
        exe_path = r"C:\Windows\System32\notepad.exe"
        title = "Notepad"
        shot_name = "notepad_live_proof.png"

    res = send_ipc_request({
        "Command": "launch_app",
        "ExePath": exe_path,
        "AppTitle": title,
        "ScreenshotName": shot_name
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def login_status() -> str:
    """Query whether LoginDialog or MainWindow is currently active, discovered controls, and window evidence."""
    res = send_ipc_request({"Command": "login_status"})
    return json.dumps(res, indent=2)

@mcp.tool()
def fill_login_username(username: str = "") -> str:
    """Enter username into UsernameBox in LoginDialog using UI Automation. Does not reveal password."""
    if not username:
        user_env, _ = get_test_credentials()
        username = user_env

    res = send_ipc_request({
        "Command": "fill_login_username",
        "Username": username
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def fill_login_password() -> str:
    """Securely enter test password into PasswordBox in LoginDialog via FlaUI. Never reveals password."""
    _, password = get_test_credentials()
    if not password:
        return json.dumps({
            "Success": False,
            "Status": "TEST CREDENTIALS REQUIRED",
            "Message": "Test password could not be resolved from environment or test fixture."
        }, indent=2)

    res = send_ipc_request({
        "Command": "fill_login_password",
        "Password": password
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def submit_login() -> str:
    """Invoke LoginButton in LoginDialog via UI Automation, capturing pre-submit screenshot."""
    res = send_ipc_request({"Command": "submit_login"})
    return json.dumps(res, indent=2)

@mcp.tool()
def wait_for_login_result(timeout_seconds: int = 15) -> str:
    """Wait for MainWindow to become discoverable after submitting login, capturing post-login proof."""
    res = send_ipc_request({
        "Command": "wait_for_login_result",
        "TimeoutSeconds": timeout_seconds
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def login_with_test_credentials() -> str:
    """Complete semantic workflow: populate test credentials into controls, submit, verify MainWindow, capture evidence."""
    username, password = get_test_credentials()
    if not username or not password:
        return json.dumps({
            "Success": False,
            "Status": "TEST CREDENTIALS REQUIRED",
            "Message": "Test credentials could not be resolved from environment or test fixture."
        }, indent=2)

    res = send_ipc_request({
        "Command": "login_with_test_credentials",
        "Username": username,
        "Password": password,
        "AppTitle": "Login"
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def inspect_window(app_title: str = "Smart Weighbridge") -> str:
    """Inspect active window controls, automation tree, and navigation controls."""
    res = send_ipc_request({
        "Command": "inspect_window",
        "AppTitle": app_title,
        "ScreenshotName": "inspect_proof.png"
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def type_text(target_auto_id: str, text: str) -> str:
    """Type text into a target control by AutomationId."""
    res = send_ipc_request({
        "Command": "type_text",
        "TargetAutomationId": target_auto_id,
        "TextToType": text,
        "ScreenshotName": "interaction_proof.png"
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def click_control(target_auto_id: str = "", target_name: str = "") -> str:
    """Click a UI control by AutomationId or Name."""
    res = send_ipc_request({
        "Command": "click_control",
        "TargetAutomationId": target_auto_id,
        "TargetName": target_name,
        "ScreenshotName": "click_proof.png"
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def close_app() -> str:
    """Close active application launched by Desktop Automation Host."""
    res = send_ipc_request({"Command": "close_app"})
    return json.dumps(res, indent=2)

# =========================================================================
# VEHICLE ENTRY SEMANTIC MCP TOOLS
# =========================================================================

@mcp.tool()
def navigate_to_vehicle_entry() -> str:
    """Navigate to the Vehicle Entry screen using the real UI navigation control in MainWindow."""
    res = send_ipc_request({"Command": "navigate_to_vehicle_entry"})
    return json.dumps(res, indent=2)

@mcp.tool()
def inspect_vehicle_entry() -> str:
    """Inspect the live Vehicle Entry view: enumerate all visible controls with AutomationId, Type, Name, State, Value."""
    res = send_ipc_request({"Command": "inspect_vehicle_entry"})
    return json.dumps(res, indent=2)

@mcp.tool()
def get_vehicle_entry_state() -> str:
    """Read the current operational state from the live Vehicle Entry screen."""
    res = send_ipc_request({"Command": "get_vehicle_entry_state"})
    return json.dumps(res, indent=2)

@mcp.tool()
def set_vehicle_number(vehicle_number: str) -> str:
    """Enter vehicle number into the 'Vehicle No' ComboBox/control on the Vehicle Entry screen."""
    res = send_ipc_request({
        "Command": "set_vehicle_number",
        "VehicleNumber": vehicle_number
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def select_party(party_name: str) -> str:
    """Select or enter customer/supplier party name into 'Party Name' control."""
    res = send_ipc_request({
        "Command": "select_party",
        "PartyName": party_name
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def select_material(material_name: str) -> str:
    """Select or enter commodity material into 'Material' control."""
    res = send_ipc_request({
        "Command": "select_material",
        "MaterialName": material_name
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def select_vehicle_type(vehicle_type: str) -> str:
    """Select vehicle classification type into 'Vehicle Type' control."""
    res = send_ipc_request({
        "Command": "select_vehicle_type",
        "VehicleTypeName": vehicle_type
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def get_displayed_weight() -> str:
    """Read the digital scale HUD on the Vehicle Entry screen (Live weight, stability status, source)."""
    res = send_ipc_request({"Command": "get_displayed_weight"})
    return json.dumps(res, indent=2)

@mcp.tool()
def execute_first_weighment(weight_kg: float = 25000.0, is_stable: bool = True) -> str:
    """Provide simulated weight, capture via [F3], submit First Weighment [F5], verify state transition."""
    res = send_ipc_request({
        "Command": "execute_first_weighment",
        "WeightKg": weight_kg,
        "IsStable": is_stable
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def execute_second_weighment(slip_number: str = "", weight_kg: float = 10000.0, is_stable: bool = True) -> str:
    """Switch to F2, search ticket, provide second weight (e.g. Tare), capture via [F3], submit [F5], verify completed."""
    res = send_ipc_request({
        "Command": "execute_second_weighment",
        "SlipNumber": slip_number,
        "WeightKg": weight_kg,
        "IsStable": is_stable
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def wait_for_weighment_state(expected_state: str, timeout_seconds: int = 15) -> str:
    """Wait for Vehicle Entry screen status or badge to transition to the expected state."""
    res = send_ipc_request({
        "Command": "wait_for_weighment_state",
        "ExpectedState": expected_state,
        "TimeoutSeconds": timeout_seconds
    })
    return json.dumps(res, indent=2)

@mcp.tool()
def verify_weighment_persistence(slip_number: str = "", vehicle_number: str = "") -> str:
    """Directly query the SQLite database to verify persisted weighment state, weights, and timestamps."""
    if not os.path.exists(DB_PATH):
        return json.dumps({
            "Success": False,
            "Status": "DB_NOT_FOUND",
            "Message": f"Database file not found at {DB_PATH}"
        }, indent=2)

    try:
        conn = sqlite3.connect(DB_PATH, timeout=10.0)
        conn.row_factory = sqlite3.Row
        cur = conn.cursor()

        if slip_number:
            cur.execute("SELECT * FROM Weighments WHERE SlipNumber = ?", (slip_number,))
        elif vehicle_number:
            cur.execute("SELECT * FROM Weighments WHERE VehicleNumber = ? ORDER BY Id DESC LIMIT 1", (vehicle_number,))
        else:
            cur.execute("SELECT * FROM Weighments ORDER BY Id DESC LIMIT 1")

        row = cur.fetchone()
        conn.close()

        if row is None:
            return json.dumps({
                "Success": False,
                "Status": "RECORD_NOT_FOUND",
                "Message": f"No weighment record found for SlipNumber='{slip_number}', VehicleNumber='{vehicle_number}'"
            }, indent=2)

        data = dict(row)
        # Convert byte/guid/timestamp objects if needed
        for k, v in data.items():
            if isinstance(v, bytes):
                data[k] = v.hex()

        status_names = {0: "Created", 1: "AwaitingSecondWeight", 2: "Completed", 3: "Cancelled"}
        mode_names = {0: "GrossFirst", 1: "TareFirst", 2: "Direct"}

        status_code = data.get("Status")
        mode_code = data.get("Mode")

        return json.dumps({
            "Success": True,
            "Status": "VERIFIED_IN_DB",
            "SlipNumber": data.get("SlipNumber"),
            "VehicleNumber": data.get("VehicleNumber"),
            "PartyName": data.get("PartyName"),
            "MaterialName": data.get("MaterialName"),
            "StatusCode": status_code,
            "StatusName": status_names.get(status_code, str(status_code)),
            "ModeCode": mode_code,
            "ModeName": mode_names.get(mode_code, str(mode_code)),
            "FirstWeightGrams": data.get("FirstWeightGrams"),
            "FirstWeightKg": (data.get("FirstWeightGrams") or 0) / 1000.0,
            "SecondWeightGrams": data.get("SecondWeightGrams"),
            "SecondWeightKg": (data.get("SecondWeightGrams") or 0) / 1000.0 if data.get("SecondWeightGrams") else None,
            "NetWeightGrams": data.get("NetWeightGrams"),
            "NetWeightKg": (data.get("NetWeightGrams") or 0) / 1000.0 if data.get("NetWeightGrams") else None,
            "FirstWeightAtUtc": data.get("FirstWeightAtUtc"),
            "SecondWeightAtUtc": data.get("SecondWeightAtUtc"),
            "CompletedAtUtc": data.get("CompletedAtUtc"),
            "RecordDetails": data
        }, indent=2)
    except Exception as ex:
        return json.dumps({
            "Success": False,
            "Status": "DB_EXCEPTION",
            "Message": f"Error querying SQLite database: {str(ex)}"
        }, indent=2)

@mcp.tool()
def capture_vehicle_entry_evidence(evidence_name: str = "vehicle_entry_evidence") -> str:
    """Capture full window screenshot and diagnostics under test-artifacts\\desktop-automation\\vehicle-entry\\"""
    res = send_ipc_request({
        "Command": "capture_vehicle_entry_evidence",
        "ScreenshotName": evidence_name
    })
    return json.dumps(res, indent=2)

if __name__ == "__main__":
    mcp.run()