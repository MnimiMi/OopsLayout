import Foundation

/// Tiny append-only logger for diagnosing the keyboard backend.
/// Writes to /tmp/oopslayout-debug.log. OFF by default — when enabled it records
/// every keystroke, so only flip this on for local debugging, never in a release.
enum DebugLog {
    static var enabled = false
    private static let path = "/tmp/oopslayout-debug.log"
    private static let queue = DispatchQueue(label: "com.oopslayout.debuglog")

    static func write(_ message: String) {
        guard enabled else { return }
        let line = "[\(Date())] \(message)\n"
        queue.async {
            if let data = line.data(using: .utf8) {
                if let fh = FileHandle(forWritingAtPath: path) {
                    fh.seekToEndOfFile()
                    fh.write(data)
                    fh.closeFile()
                } else {
                    try? data.write(to: URL(fileURLWithPath: path))
                }
            }
        }
    }

    static func reset() {
        guard enabled else { return }
        try? "".write(toFile: path, atomically: true, encoding: .utf8)
    }
}
