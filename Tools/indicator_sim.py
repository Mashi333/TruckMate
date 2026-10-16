#!/usr/bin/env python3
"""
indicator_sim.py -- Weighing-indicator simulator for the TruckMate weighbridge (Part 1).

It emulates a continuous-output weighing indicator so the whole Part 1 chain can be
tested without hardware:

    Indicator --RS-232--> Weighbridge PC / Edge Agent --HTTPS--> Server --Frontend

What it does
------------
* Runs a realistic truck cycle: empty -> drive on (~4 s ramp) -> overshoot and damped
  oscillation (~4 s) -> perfectly still stable hold -> drive off (~3 s) -> empty.
* Emits lines at a configurable rate (default 10 lines/sec), CR+LF terminated.
* Supports selectable line formats (easily extensible for other indicator brands).
* Realism: rounds to a configurable scale division (default 5 kg), adds motion noise
  only while moving, clamps negatives to 0, and repeats the exact same value while
  stable (so an agent that counts "N identical readings" sees stability).
* Output targets (can be combined):
    - screen : print the raw lines to the console (no setup)
    - serial : write the lines to a serial port, e.g. a com0com pair (needs pyserial)
    - http   : POST JSON to the server weight API, e.g. to test the server + frontend
               without the agent (supports a self-signed HTTPS certificate)
* Interactive commands while running (type + Enter):
    t <kg>  truck arrives (optional weight; random within min/max if omitted)
    l       truck leaves
    z       reset to zero
    p / r   pause / resume output ("unplug" the cable)
    g       emit one garbage line (test parser resilience)
    h       help
    q       quit
* --auto mode repeats trucks with random weights (min/max, hold and idle times) for
  soak testing.
* Keeps a steady reading rate using a monotonic clock. Command input and HTTP posting
  run on background threads so they never block the timing loop.
* Never crashes on bad input and keeps running if the server is unreachable.

Only pyserial is third-party, and only for --target serial. Everything else uses the
standard library.

Examples
--------
Screen only, auto trucks:
    python indicator_sim.py

Direct POST to the server API (accept self-signed cert):
    python indicator_sim.py -o http --url https://localhost:7001/api/weight \
        --api-key dev-secret --scale-id WB1 --insecure

Through a virtual COM pair (com0com) to the real edge agent:
    python indicator_sim.py -o serial --port COM10 --baud 9600 --echo

Soak test, 4 a.m. weight range, 8 s hold:
    python indicator_sim.py --auto --min-weight 8000 --max-weight 38000 \
        --hold-time 8 --idle-time 5
"""

from __future__ import annotations

import argparse
import json
import math
import queue
import random
import ssl
import sys
import threading
import time
import urllib.request

# --------------------------------------------------------------------------------------
# Line formats
# --------------------------------------------------------------------------------------
# A format is a function: (weight_kg, stable, division, unit) -> str
# Register new indicator brands by writing a function and adding it to FORMATS.
# The line is terminated with CR+LF by the sinks, not by the formatter.

FORMATS: dict[str, object] = {}


def _decimals_for(division: float) -> int:
    """Number of decimal places implied by the scale division (5 -> 0, 0.5 -> 1)."""
    text = f"{division:.10f}".rstrip("0")
    return len(text.split(".")[1]) if "." in text else 0


def _split_weight(weight_kg: float, decimals: int) -> tuple[str, str, str]:
    """Return (sign, integer-part-digits, fractional-part-digits) for a weight."""
    sign = "-" if weight_kg < 0 else "+"
    value = abs(weight_kg)
    text = f"{value:.{decimals}f}"
    if decimals:
        integer, frac = text.split(".")
    else:
        integer, frac = text, ""
    return sign, integer, frac


def fmt_toledo(weight_kg: float, stable: bool, division: float, unit: str = "kg") -> str:
    """Toledo/MT style: 'ST,GS,+001234.5kg' when stable, 'US,GS,+001234.5kg' otherwise."""
    flag = "ST" if stable else "US"
    sign, integer, frac = _split_weight(weight_kg, 1)
    number = f"{sign}{int(integer):06d}"
    if frac:
        number += f".{frac}"
    return f"{flag},GS,{number}{unit}"


def fmt_plain(weight_kg: float, stable: bool, division: float, unit: str = "kg") -> str:
    """Plain style with no stability flag: '+  1234 kg' (width follows the division)."""
    decimals = _decimals_for(division)
    sign, integer, frac = _split_weight(weight_kg, decimals)
    if decimals:
        body = f"{int(integer) if integer else 0}.{frac}"
        body = body.rjust(6 + 1 + decimals)
    else:
        body = integer.rjust(6)
    return f"{sign}{body} {unit}"


FORMATS["toledo"] = fmt_toledo
FORMATS["plain"] = fmt_plain


# --------------------------------------------------------------------------------------
# Truck-cycle state machine
# --------------------------------------------------------------------------------------
# States
IDLE, RAMP, SETTLE, HOLD, LEAVE = range(5)


def _smoothstep(x: float) -> float:
    """Ease-in/out curve in [0, 1] for the drive-on / drive-off motion."""
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


class TruckController:
    """Advances the emulated weight through the truck cycle and reports stability."""

    def __init__(
        self,
        division: float,
        ramp_time: float,
        settle_time: float,
        leave_time: float,
        noise_frac: float,
        rng: random.Random,
    ) -> None:
        self.division = division
        self.ramp_time = max(0.05, ramp_time)
        self.settle_time = max(0.05, settle_time)
        self.leave_time = max(0.05, leave_time)
        self.noise_frac = noise_frac
        self.rng = rng

        self.state = IDLE
        self.t = 0.0
        self.target = 0.0
        self.start_weight = 0.0

        # damped-oscillation shape for the settling phase
        self._osc_hz = 1.2
        self._tau = self.settle_time / 4.0

    # -- rounding helper ---------------------------------------------------------------
    def _round(self, weight: float) -> float:
        return round(round(weight / self.division) * self.division, 6)

    # -- commands ----------------------------------------------------------------------
    def arrive(self, target_kg: float) -> None:
        """Start the drive-on ramp toward target_kg from the current weight."""
        self.start_weight = self.raw_weight()
        self.target = max(0.0, target_kg)
        self.state = RAMP
        self.t = 0.0

    def leave(self) -> None:
        """Start the drive-off ramp from the current weight back to empty."""
        self.start_weight = self.raw_weight()
        self.state = LEAVE
        self.t = 0.0

    def reset_zero(self) -> None:
        self.state = IDLE
        self.t = 0.0
        self.target = 0.0
        self.start_weight = 0.0

    # -- evolution ---------------------------------------------------------------------
    def update(self, dt: float) -> None:
        self.t += dt
        if self.state == RAMP and self.t >= self.ramp_time:
            self.state = SETTLE
            self.t = 0.0
        elif self.state == SETTLE and self.t >= self.settle_time:
            self.state = HOLD
            self.t = 0.0
        elif self.state == LEAVE and self.t >= self.leave_time:
            self.state = IDLE
            self.t = 0.0
            self.target = 0.0
            self.start_weight = 0.0

    def raw_weight(self) -> float:
        """Unrounded, un-noised weight for the current phase."""
        if self.state == IDLE:
            return 0.0
        if self.state == RAMP:
            progress = _smoothstep(self.t / self.ramp_time)
            return self.start_weight + (self.target - self.start_weight) * progress
        if self.state == SETTLE:
            amplitude = max(self.division * 3.0, self.target * 0.06)
            decay = math.exp(-self.t / self._tau)
            return self.target + amplitude * decay * math.cos(2 * math.pi * self._osc_hz * self.t)
        if self.state == HOLD:
            return self.target
        if self.state == LEAVE:
            progress = _smoothstep(self.t / self.leave_time)
            return self.start_weight * (1.0 - progress)
        return 0.0

    def _noise_sigma(self) -> float:
        if self.state in (IDLE, HOLD):
            return 0.0
        base = max(self.division * 1.5, self.target * self.noise_frac)
        if self.state == SETTLE:
            base *= math.exp(-self.t / self._tau)
        return base

    def reading(self) -> tuple[float, bool]:
        """Return (weight_kg, stable) for this instant, rounded and clamped."""
        weight = self.raw_weight()
        sigma = self._noise_sigma()
        if sigma > 0.0:
            weight += self.rng.gauss(0.0, sigma)
        if weight < 0.0:
            weight = 0.0
        return self._round(weight), self.state in (IDLE, HOLD)


# --------------------------------------------------------------------------------------
# Output sinks
# --------------------------------------------------------------------------------------
class ScreenSink:
    """Prints raw indicator lines to the console."""

    def __init__(self, prefix: str = "") -> None:
        self.prefix = prefix

    def write(self, line: str) -> None:
        sys.stdout.write(f"{self.prefix}{line}\n")
        sys.stdout.flush()

    def garbage(self) -> None:
        self.write("@#!%$ GARBAGE LINE @#!%$")


class SerialSink:
    """Writes CR+LF terminated lines to a serial port (com0com, socat, USB adapter...)."""

    _PARITY = {"N": "PARITY_NONE", "E": "PARITY_EVEN", "O": "PARITY_ODD",
               "M": "PARITY_MARK", "S": "PARITY_SPACE"}

    def __init__(self, port: str, baud: int, bytesize: int, parity: str, stopbits: float) -> None:
        try:
            import serial  # pyserial, only needed for serial output
        except ImportError as exc:  # pragma: no cover - depends on environment
            raise SystemExit(
                "Serial output needs pyserial. Install it with:  pip install pyserial"
            ) from exc
        self._serial = serial
        stop = {1: serial.STOPBITS_ONE, 1.5: serial.STOPBITS_ONE_POINT_FIVE,
                2: serial.STOPBITS_TWO}[stopbits]
        parity_const = getattr(serial, self._PARITY[parity])
        # serial_for_url also accepts loop:// and other pseudo-ports, handy for testing.
        self.ser = serial.serial_for_url(
            port, baudrate=baud, bytesize=bytesize, parity=parity_const,
            stopbits=stop, timeout=1.0,
        )
        self._encoding = "ascii"

    def write(self, line: str) -> None:
        try:
            self.ser.write(line.encode(self._encoding, errors="replace") + b"\r\n")
        except Exception as exc:  # keep running if the cable is yanked
            sys.stderr.write(f"\n[serial] write failed: {exc}\n")

    def garbage(self) -> None:
        try:
            self.ser.write(b"\xff\xfe\x00 GARBAGE @@@\r\n")
        except Exception as exc:
            sys.stderr.write(f"\n[serial] garbage write failed: {exc}\n")

    def close(self) -> None:
        try:
            self.ser.close()
        except Exception:
            pass


class HttpPoster(threading.Thread):
    """Background worker that POSTs readings to the server weight API.

    Runs on its own thread with a bounded queue: if the server is down or slow the
    queue drops readings instead of blocking the timing loop. Never raises.
    """

    def __init__(
        self,
        url: str,
        api_key: str,
        scale_id: str,
        insecure: bool,
        stop_event: threading.Event,
        maxsize: int = 200,
    ) -> None:
        super().__init__(name="http-poster", daemon=True)
        self.url = url
        self.api_key = api_key
        self.scale_id = scale_id
        self.stop_event = stop_event
        self.queue: queue.Queue = queue.Queue(maxsize=maxsize)
        self.context = ssl._create_unverified_context() if insecure else None
        self._error_logged = False
        self.posted = 0
        self.dropped = 0
        self.failed = 0

    def submit(self, weight_kg: float, stable: bool) -> None:
        payload = {
            "scaleId": self.scale_id,
            "weightKg": weight_kg,
            "stable": stable,
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        }
        try:
            self.queue.put_nowait(payload)
        except queue.Full:
            self.dropped += 1

    def run(self) -> None:
        while not self.stop_event.is_set():
            try:
                payload = self.queue.get(timeout=0.2)
            except queue.Empty:
                continue
            if payload is None:
                break
            self._post(payload)

    def _post(self, payload: dict) -> None:
        body = json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(
            self.url, data=body, method="POST",
            headers={"Content-Type": "application/json", "X-Api-Key": self.api_key},
        )
        for attempt in range(2):  # one retry
            try:
                with urllib.request.urlopen(request, timeout=4.0, context=self.context) as resp:
                    resp.read()
                self.posted += 1
                if self._error_logged:
                    sys.stderr.write("\n[http] connection restored\n")
                    self._error_logged = False
                return
            except Exception as exc:
                if attempt == 0:
                    time.sleep(0.2)
                    continue
                self.failed += 1
                if not self._error_logged:
                    sys.stderr.write(f"\n[http] POST failed ({exc}); still running\n")
                    self._error_logged = True


# --------------------------------------------------------------------------------------
# Command input
# --------------------------------------------------------------------------------------
HELP_TEXT = """\
Commands:
  t <kg>   truck arrives (weight optional; random in min/max if omitted)
  l        truck leaves
  z        reset to zero
  p        pause output (simulate unplugged cable)
  r        resume output
  g        emit one garbage line (parser resilience test)
  h        show this help
  q        quit
"""


def command_reader(cmd_queue: "queue.Queue[str]", stop_event: threading.Event) -> None:
    """Background thread: read lines from stdin and hand them to the main loop."""
    while not stop_event.is_set():
        try:
            line = sys.stdin.readline()
        except Exception:
            break
        if not line:  # EOF / stdin closed
            break
        cmd_queue.put(line.strip())


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------
def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="indicator_sim.py",
        description="Simulate a continuous-output weighing indicator for TruckMate Part 1.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=HELP_TEXT,
    )
    parser.add_argument("-o", "--target", default="screen",
                        help="output target(s): screen, serial, http (comma separated). Default: screen")
    parser.add_argument("--format", default="toledo", choices=sorted(FORMATS),
                        help="line format. Default: toledo (ST,GS,+001234.5kg)")
    parser.add_argument("--rate", type=float, default=10.0, help="lines per second. Default: 10")
    parser.add_argument("--division", type=float, default=5.0, help="scale division in kg. Default: 5")
    parser.add_argument("--unit", default="kg", help="unit text in the line. Default: kg")
    parser.add_argument("--echo", action="store_true",
                        help="also print lines to the screen when target is serial/http")

    parser.add_argument("--port", help="serial port for --target serial (e.g. COM10)")
    parser.add_argument("--baud", type=int, default=9600, help="serial baud rate. Default: 9600")
    parser.add_argument("--bytesize", type=int, default=8, choices=[5, 6, 7, 8],
                        help="serial data bits. Default: 8")
    parser.add_argument("--parity", default="N", choices=list(SerialSink._PARITY),
                        help="serial parity: N/E/O/M/S. Default: N")
    parser.add_argument("--stopbits", type=float, default=1, choices=[1, 1.5, 2],
                        help="serial stop bits. Default: 1")

    parser.add_argument("--url", help="weight API URL for --target http")
    parser.add_argument("--api-key", default="dev-secret", help="X-Api-Key value for HTTP posting")
    parser.add_argument("--scale-id", default="WB1", help="scale identifier. Default: WB1")
    parser.add_argument("--insecure", action="store_true",
                        help="accept a self-signed HTTPS certificate")

    parser.add_argument("--auto", action="store_true", help="repeat trucks automatically (soak test)")
    parser.add_argument("--min-weight", type=float, default=5000.0, help="auto min weight kg")
    parser.add_argument("--max-weight", type=float, default=40000.0, help="auto max weight kg")
    parser.add_argument("--hold-time", type=float, default=8.0, help="auto stable hold seconds")
    parser.add_argument("--idle-time", type=float, default=5.0, help="auto idle seconds between trucks")
    parser.add_argument("--ramp-time", type=float, default=4.0, help="drive-on ramp seconds")
    parser.add_argument("--settle-time", type=float, default=4.0, help="overshoot/settle seconds")
    parser.add_argument("--leave-time", type=float, default=3.0, help="drive-off ramp seconds")
    parser.add_argument("--noise", type=float, default=0.004,
                        help="motion noise as a fraction of target. Default: 0.004")
    parser.add_argument("--seed", type=int, default=None, help="random seed (reproducible tests)")
    return parser


def _parse_targets(value: str) -> list[str]:
    targets = [t.strip().lower() for t in value.split(",") if t.strip()]
    valid = {"screen", "serial", "http"}
    bad = [t for t in targets if t not in valid]
    if bad:
        raise SystemExit(f"Unknown target(s): {', '.join(bad)}. Use screen, serial or http.")
    if not targets:
        targets = ["screen"]
    return targets


# --------------------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------------------
def main(argv: "list[str] | None" = None) -> int:
    args = build_parser().parse_args(argv)
    targets = _parse_targets(args.target)
    if "serial" in targets and not args.port:
        raise SystemExit("--target serial requires --port (e.g. --port COM10)")
    if "http" in targets and not args.url:
        raise SystemExit("--target http requires --url (e.g. --url https://host/api/weight)")
    if args.rate <= 0:
        raise SystemExit("--rate must be greater than 0")
    if args.division <= 0:
        raise SystemExit("--division must be greater than 0")
    if args.min_weight > args.max_weight:
        args.min_weight, args.max_weight = args.max_weight, args.min_weight

    rng = random.Random(args.seed)
    formatter = FORMATS[args.format]

    sinks: list[object] = []
    if "screen" in targets or args.echo:
        sinks.append(ScreenSink())
    if "serial" in targets:
        sinks.append(SerialSink(args.port, args.baud, args.bytesize, args.parity, args.stopbits))

    stop_event = threading.Event()
    poster = None
    if "http" in targets:
        poster = HttpPoster(args.url, args.api_key, args.scale_id, args.insecure, stop_event)

    controller = TruckController(
        division=args.division,
        ramp_time=args.ramp_time,
        settle_time=args.settle_time,
        leave_time=args.leave_time,
        noise_frac=args.noise,
        rng=rng,
    )

    # Banner
    print("TruckMate indicator simulator")
    print(f"  targets : {', '.join(targets)}")
    print(f"  format  : {args.format}   rate: {args.rate:g}/s   division: {args.division:g} {args.unit}")
    if "serial" in targets:
        print(f"  serial  : {args.port} {args.baud} {args.bytesize}{args.parity}{args.stopbits:g}")
    if "http" in targets:
        print(f"  http    : {args.url}  scale={args.scale_id}  insecure={args.insecure}")
    print(f"  mode    : {'AUTO' if args.auto else 'MANUAL'}   type 'h' for help, 'q' to quit")
    print("-" * 60)

    cmd_queue: "queue.Queue[str]" = queue.Queue()
    cmd_thread = threading.Thread(target=command_reader, args=(cmd_queue, stop_event),
                                  name="command-reader", daemon=True)
    cmd_thread.start()
    if poster is not None:
        poster.start()

    runtime = RuntimeState()
    interval = 1.0 / args.rate
    next_tick = time.monotonic()
    last_tick = next_tick
    emitted = 0

    try:
        while not stop_event.is_set():
            now = time.monotonic()
            if now < next_tick:
                time.sleep(min(next_tick - now, 0.02))
                continue
            dt = now - last_tick
            last_tick = now
            next_tick += interval
            if now - next_tick > 1.0:  # fell far behind; resynchronise
                next_tick = now + interval

            # --- process interactive commands (non-blocking) ---
            while True:
                try:
                    raw = cmd_queue.get_nowait()
                except queue.Empty:
                    break
                _handle_command(raw, controller, args, sinks, rng, runtime)
            if runtime.stop:
                stop_event.set()
                break

            _auto_schedule(args, controller, rng)

            # advance physics even while "unplugged" (the truck still moves)
            controller.update(dt)

            if not runtime.paused:
                weight, stable = controller.reading()
                line = formatter(weight, stable, args.division, args.unit)
                for sink in sinks:
                    sink.write(line)
                if poster is not None:
                    poster.submit(weight, stable)
                emitted += 1
    except KeyboardInterrupt:
        pass
    finally:
        stop_event.set()
        try:
            cmd_queue.put_nowait("")
        except queue.Full:
            pass
        if poster is not None:
            poster.join(timeout=1.0)
            print(f"\n[http] posted={poster.posted} failed={poster.failed} dropped={poster.dropped}")
        for sink in sinks:
            close = getattr(sink, "close", None)
            if callable(close):
                close()
        print(f"\nStopped after emitting {emitted} line(s).")
    return 0


# --------------------------------------------------------------------------------------
# Command handling
# --------------------------------------------------------------------------------------
class RuntimeState:
    """Mutable run flags shared between the command handler and the timing loop."""

    def __init__(self) -> None:
        self.paused = False
        self.stop = False


def _handle_command(raw: str, controller: TruckController, args: argparse.Namespace,
                    sinks: list, rng: random.Random, runtime: RuntimeState) -> None:
    raw = raw.strip()
    if not raw:
        return
    parts = raw.split()
    cmd = parts[0].lower()
    if cmd == "q":
        runtime.stop = True
    elif cmd == "h":
        print(HELP_TEXT)
    elif cmd == "p":
        runtime.paused = True
        print("[paused] output suspended (cable unplugged)")
    elif cmd == "r":
        runtime.paused = False
        print("[resumed] output restored")
    elif cmd == "z":
        controller.reset_zero()
        print("[zero] reset to empty")
    elif cmd == "t":
        if len(parts) > 1:
            try:
                weight = float(parts[1])
            except ValueError:
                print(f"[warn] not a number: {parts[1]!r}; using random weight")
                weight = rng.uniform(args.min_weight, args.max_weight)
        else:
            weight = rng.uniform(args.min_weight, args.max_weight)
        controller.arrive(weight)
        print(f"[arrive] truck target {weight:.1f} kg")
    elif cmd == "l":
        controller.leave()
        print("[leave] truck driving off")
    elif cmd == "g":
        print("[garbage] sending one invalid line")
        for sink in sinks:
            sink.garbage()
    else:
        print(f"[?] unknown command {cmd!r}; type 'h' for help")


def _auto_schedule(args: argparse.Namespace, controller: TruckController,
                   rng: random.Random) -> None:
    if not args.auto:
        return
    if controller.state == IDLE and controller.t >= args.idle_time:
        controller.arrive(rng.uniform(args.min_weight, args.max_weight))
    elif controller.state == HOLD and controller.t >= args.hold_time:
        controller.leave()


if __name__ == "__main__":
    sys.exit(main())
