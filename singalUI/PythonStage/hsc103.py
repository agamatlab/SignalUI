import argparse
import sys
import time
import warnings
from types import MethodType
from typing import Tuple, Union, Sequence, Any
import serial

_hsc103_opened_objects = dict()


def _decode_readline(raw: bytes) -> str:
    """Controller lines are mostly ASCII; pyserial can see CP1252-ish bytes (e.g. 0x92) or noise — never fail UTF-8."""
    if not raw:
        return ""
    return raw.decode("latin-1", errors="replace").rstrip("\r\n")


# Pulse ↔ angle for a motorized rotation stage on HSC-103.
# As per page 41 of HSC-103_EN.pdf: "Set a rotary angle with the integer of the 0.0001 degrees unit."
DEFAULT_ROTATION_DEGREES_PER_PULSE = 0.00001

class OptosigmaWarning(Warning):
    pass

class HSC103(serial.Serial):

    def __init__(self, port=None):
        # port=None here so pyserial does not open in __init__; we open in HSC103.open() after self.port is set.
        super().__init__(port=None, baudrate=38400, bytesize=serial.EIGHTBITS, parity=serial.PARITY_NONE,
                         stopbits=serial.STOPBITS_ONE, timeout=1, xonxoff=False, rtscts=False, write_timeout=None,
                         dsrdtr=False, inter_byte_timeout=None, exclusive=None)
        self.port = port

    def open(self):
        if self.port not in _hsc103_opened_objects:
            super().open()
            if self.is_open:
                _hsc103_opened_objects[self.port] = self

    def close(self):
        super().close()
        if self.port in _hsc103_opened_objects:
            del _hsc103_opened_objects[self.port]

    def raw_command(self, cmd: str) -> Union[bool, Any]:
        self.write(cmd.encode())
        self.write(b"\r\n")

        if cmd[:2] in ["Q:", "!:", "?:"]:
            ret = _decode_readline(self.readline())
            if ret != "":
                return ret
            return "NG"
        else:
            ret = _decode_readline(self.readline())
            if len(ret) >= 2:
                return ret[:2] == "OK"
            return False

    def set_speed(self, axis, start, bound, accel_time):
        self.raw_command(f"D:{axis},{start},{bound},{accel_time}")

    def get_position(self, axis=1, resolution=0.00001):
        status = self.raw_command('Q:').split(',')
        return int(status[axis - 1]) * resolution

    def set_position(self, axis=1, target=0, resolution=0.00001):
        current = self.get_position(axis, resolution)
        relative = int((target - current) / resolution)
        status = False
        
        if axis == 1:
            status = self.raw_command(f'M:{relative}')
        elif axis == 2:
            status = self.raw_command(f'M:0,{relative}')
        elif axis == 3:
            status = self.raw_command(f'M:0,0,{relative}')
            
        if status:
            print(f"OPTOSIGMA >> Axis [{axis}] - Moving to {target}...")
            # Note: HSC-103_EN.pdf page 23 states: "Unlike the SHOT-Controller, the activation 
            # command (G) is not needed in HSC-103. Activation command (G) is treated as an incorrect command"
            print(f"OPTOSIGMA >> Axis [{axis}] - Waiting for operation to complete...")
            while self.get_busy(axis):
                time.sleep(0.1)
        return status

    def return_origin(self, axis=1):
        status = False
        if axis == 1:
            status = self.raw_command("H:1")
        elif axis == 2:
            status = self.raw_command("H:0,1")
        elif axis == 3:
            status = self.raw_command("H:0,0,1")
            
        if status:
            print(f"OPTOSIGMA >> Axis [{axis}] - Moving to machine origin...")
            print(f"OPTOSIGMA >> Axis [{axis}] - Waiting for operation to complete...")
            while self.get_busy(axis):
                time.sleep(0.1)
        return status

    def return_rotation_origin(self, axis=1):
        """Home the rotation axis to mechanical origin (H:); position reads as 0° after homing if origin is zero."""
        self.return_origin(axis=axis)

    def get_angle_degrees(self, axis=1, degrees_per_pulse=DEFAULT_ROTATION_DEGREES_PER_PULSE):
        """Current angle (degrees) from pulse count Q: using the stage’s degrees-per-pulse factor."""
        return self.get_position(axis=axis, resolution=degrees_per_pulse)

    def set_angle_degrees(self, axis=1, target_degrees=0.0, degrees_per_pulse=DEFAULT_ROTATION_DEGREES_PER_PULSE):
        """Move rotation axis to an absolute angle (degrees); same M: sequence as set_position."""
        return self.set_position(axis=axis, target=target_degrees, resolution=degrees_per_pulse)

    def stop(self, axis=1):
        """Decelerate and stop movement on the specified axis (L command)."""
        status = False
        if axis == 1:
            status = self.raw_command("L:1")
        elif axis == 2:
            status = self.raw_command("L:0,1")
        elif axis == 3:
            status = self.raw_command("L:0,0,1")
        return status

    def is_valid(self):
        status = self.raw_command('?:N')
        return "HSC-103" in status

    def get_busy(self, axis=1):
        status = self.raw_command('!:').split(',')
        return status[axis - 1] == '1'


def main():
    parser = argparse.ArgumentParser(
        description=(
            "OptoSigma HSC-103: rotation stage - home to origin (0 deg), move to target angle, hold, then stop and close."
        )
    )
    parser.add_argument("--port", default="COM3", help="Serial port (default: COM3)")
    parser.add_argument("--axis", type=int, default=1, choices=[1, 2, 3], help="Axis index (1-3)")
    parser.add_argument(
        "--degrees-per-pulse",
        type=float,
        default=DEFAULT_ROTATION_DEGREES_PER_PULSE,
        metavar="D",
        help=(
            "Degrees represented by one controller pulse for this rotation stage "
            f"(default: {DEFAULT_ROTATION_DEGREES_PER_PULSE!r}; set from manual if wrong)."
        ),
    )
    parser.add_argument(
        "--angle",
        type=float,
        default=10,
        help="Absolute angle in degrees after homing (default: 15.0).",
    )
    parser.add_argument(
        "--hold-time",
        type=float,
        default=10.0,
        help="Time in seconds to hold at the target angle before stopping (default: 30.0).",
    )
    args = parser.parse_args()

    stage = HSC103(port=args.port)
    try:
        try:
            stage.open()
        except serial.SerialException as e:
            print(
                f"OPTOSIGMA >> Could not open serial port {args.port!r}: {e}",
                file=sys.stderr,
            )
            print(
                "OPTOSIGMA >> Check Device Manager for the correct COM port, or use --port COMx.",
                file=sys.stderr,
            )
            sys.exit(1)
            
        if not stage.is_open:
            print("OPTOSIGMA >> Failed to open serial port.", file=sys.stderr)
            sys.exit(1)
        if not stage.is_valid():
            print("OPTOSIGMA >> Device not recognized (expected HSC-103).", file=sys.stderr)
            sys.exit(1)
            
        dpp = args.degrees_per_pulse
        
        print(f"OPTOSIGMA >> Starting rotation stage routine on axis {args.axis}...")

        # 1. Let the rotation stage move to zero (Home to origin)
        print("OPTOSIGMA >> Step 1: Homing rotation stage to 0 degrees...")
        stage.return_rotation_origin(axis=args.axis)
        time.sleep(0.5)  # Small safety buffer after hitting origin

        # 2. Move 15 degrees (blocking until complete)
        print(f"OPTOSIGMA >> Step 2: Moving rotation stage to {args.angle} degrees...")
        success = stage.set_angle_degrees(axis=args.axis, target_degrees=args.angle, degrees_per_pulse=dpp)
        if not success:
            print("OPTOSIGMA >> Failed to move rotation axis to target angle.", file=sys.stderr)
            sys.exit(1)

        # 3. Hold for 30 seconds (or user-defined time)
        print(f"OPTOSIGMA >> Step 3: Holding position for {args.hold_time} seconds...")
        time.sleep(args.hold_time)

        # 4. And then stop
        print("OPTOSIGMA >> Step 4: Explicitly issuing stop command to the stage...")
        stage.stop(axis=args.axis)
        
        print("OPTOSIGMA >> Routine completed successfully.")

    finally:
        stage.close()

if __name__ == "__main__":
    main()
