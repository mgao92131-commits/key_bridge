#!/usr/bin/env swift

import AppKit

let outputDirectory = URL(fileURLWithPath: CommandLine.arguments.dropFirst().first ?? "BlueType.Mac/Resources")
let fileManager = FileManager.default
try fileManager.createDirectory(at: outputDirectory, withIntermediateDirectories: true)

let pixelSize = 1024
let size = NSSize(width: pixelSize, height: pixelSize)
guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil,
    pixelsWide: pixelSize,
    pixelsHigh: pixelSize,
    bitsPerSample: 8,
    samplesPerPixel: 4,
    hasAlpha: true,
    isPlanar: false,
    colorSpaceName: .deviceRGB,
    bytesPerRow: 0,
    bitsPerPixel: 0
) else {
    fatalError("Unable to create bitmap context")
}
bitmap.size = size

func color(_ red: CGFloat, _ green: CGFloat, _ blue: CGFloat, _ alpha: CGFloat = 1) -> NSColor {
    NSColor(srgbRed: red / 255, green: green / 255, blue: blue / 255, alpha: alpha)
}

func roundedRect(_ rect: NSRect, radius: CGFloat) -> NSBezierPath {
    NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
}

func fillRounded(_ rect: NSRect, radius: CGFloat, color fill: NSColor) {
    fill.setFill()
    roundedRect(rect, radius: radius).fill()
}

func strokeRounded(_ rect: NSRect, radius: CGFloat, color stroke: NSColor, width: CGFloat) {
    let path = roundedRect(rect.insetBy(dx: width / 2, dy: width / 2), radius: radius)
    stroke.setStroke()
    path.lineWidth = width
    path.stroke()
}

func drawLine(from start: NSPoint, to end: NSPoint, color stroke: NSColor, width: CGFloat) {
    let path = NSBezierPath()
    path.move(to: start)
    path.line(to: end)
    stroke.setStroke()
    path.lineCapStyle = .round
    path.lineJoinStyle = .round
    path.lineWidth = width
    path.stroke()
}

func drawCircle(center: NSPoint, radius: CGFloat, color fill: NSColor) {
    fill.setFill()
    NSBezierPath(ovalIn: NSRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)).fill()
}

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
NSGraphicsContext.current?.imageInterpolation = .high

let canvas = NSRect(origin: .zero, size: size)
let appShape = roundedRect(canvas.insetBy(dx: 56, dy: 56), radius: 228)
let backgroundGradient = NSGradient(colors: [
    color(21, 94, 201),
    color(30, 155, 220),
    color(40, 202, 183),
])!
backgroundGradient.draw(in: appShape, angle: 135)

strokeRounded(canvas.insetBy(dx: 78, dy: 78), radius: 204, color: color(255, 255, 255, 0.22), width: 10)

let glowGradient = NSGradient(colors: [
    color(255, 255, 255, 0.30),
    color(255, 255, 255, 0.00),
])!
glowGradient.draw(in: roundedRect(NSRect(x: 142, y: 610, width: 740, height: 236), radius: 118), angle: -90)

let connector = color(220, 250, 255, 0.72)
drawLine(from: NSPoint(x: 314, y: 575), to: NSPoint(x: 705, y: 575), color: connector, width: 30)
drawLine(from: NSPoint(x: 705, y: 575), to: NSPoint(x: 705, y: 720), color: connector, width: 30)
drawLine(from: NSPoint(x: 314, y: 575), to: NSPoint(x: 314, y: 398), color: connector, width: 30)
drawCircle(center: NSPoint(x: 314, y: 575), radius: 36, color: color(240, 255, 255, 0.96))
drawCircle(center: NSPoint(x: 705, y: 575), radius: 36, color: color(240, 255, 255, 0.96))

let monitorShadow = NSShadow()
monitorShadow.shadowColor = color(0, 35, 75, 0.34)
monitorShadow.shadowBlurRadius = 24
monitorShadow.shadowOffset = NSSize(width: 0, height: -16)
monitorShadow.set()

let monitor = NSRect(x: 203, y: 274, width: 536, height: 330)
fillRounded(monitor, radius: 60, color: color(245, 252, 255, 0.98))
fillRounded(monitor.insetBy(dx: 32, dy: 32), radius: 34, color: color(8, 67, 145, 0.96))

NSShadow().set()

fillRounded(NSRect(x: 405, y: 214, width: 132, height: 66), radius: 24, color: color(225, 245, 252, 0.98))
fillRounded(NSRect(x: 338, y: 184, width: 266, height: 48), radius: 24, color: color(225, 245, 252, 0.98))

let keyColor = color(232, 252, 255, 0.92)
let accentKeyColor = color(93, 230, 207, 0.98)
let keys: [(CGFloat, CGFloat, CGFloat, CGFloat, NSColor)] = [
    (284, 482, 78, 54, keyColor),
    (382, 482, 78, 54, keyColor),
    (480, 482, 78, 54, keyColor),
    (578, 482, 78, 54, accentKeyColor),
    (304, 402, 82, 54, keyColor),
    (406, 402, 82, 54, keyColor),
    (508, 402, 82, 54, keyColor),
    (610, 402, 52, 54, keyColor),
    (352, 326, 238, 54, keyColor),
]
for key in keys {
    fillRounded(NSRect(x: key.0, y: key.1, width: key.2, height: key.3), radius: 17, color: key.4)
}

let phoneShadow = NSShadow()
phoneShadow.shadowColor = color(0, 35, 75, 0.30)
phoneShadow.shadowBlurRadius = 20
phoneShadow.shadowOffset = NSSize(width: 0, height: -14)
phoneShadow.set()

let phone = NSRect(x: 648, y: 536, width: 160, height: 276)
fillRounded(phone, radius: 42, color: color(246, 253, 255, 0.98))
fillRounded(phone.insetBy(dx: 18, dy: 22), radius: 28, color: color(11, 82, 160, 0.96))

NSShadow().set()

fillRounded(NSRect(x: 696, y: 757, width: 64, height: 10), radius: 5, color: color(224, 248, 255, 0.82))
drawCircle(center: NSPoint(x: 728, y: 566), radius: 13, color: color(224, 248, 255, 0.82))

let bPath = NSBezierPath()
bPath.move(to: NSPoint(x: 363, y: 670))
bPath.line(to: NSPoint(x: 363, y: 760))
bPath.line(to: NSPoint(x: 423, y: 760))
bPath.curve(to: NSPoint(x: 470, y: 715), controlPoint1: NSPoint(x: 453, y: 760), controlPoint2: NSPoint(x: 470, y: 742))
bPath.curve(to: NSPoint(x: 423, y: 670), controlPoint1: NSPoint(x: 470, y: 688), controlPoint2: NSPoint(x: 453, y: 670))
bPath.close()
bPath.move(to: NSPoint(x: 393, y: 698))
bPath.line(to: NSPoint(x: 419, y: 698))
bPath.curve(to: NSPoint(x: 438, y: 715), controlPoint1: NSPoint(x: 431, y: 698), controlPoint2: NSPoint(x: 438, y: 704))
bPath.curve(to: NSPoint(x: 419, y: 732), controlPoint1: NSPoint(x: 438, y: 726), controlPoint2: NSPoint(x: 431, y: 732))
bPath.line(to: NSPoint(x: 393, y: 732))
bPath.close()
color(237, 255, 255, 0.95).setFill()
bPath.windingRule = .evenOdd
bPath.fill()

NSGraphicsContext.restoreGraphicsState()

let pngURL = outputDirectory.appendingPathComponent("AppIcon-1024.png")
guard let pngData = bitmap.representation(using: .png, properties: [:]) else {
    fatalError("Unable to render AppIcon-1024.png")
}

try pngData.write(to: pngURL)
print(pngURL.path)
