# @g9tm/signalr-supernetcore-lynx for iOS. Linked into a host by Lynx Autolink (CocoaPods) from node_modules; the host
# registers the module on its LynxConfig: [config registerModule:G9SignalRLynxModule.class].
Pod::Spec.new do |s|
  s.name = 'g9tm-signalr-lynx'
  s.version = '2.10.0'
  s.summary = 'G9SignalRLynxModule: a binary WebSocket (NSURLSessionWebSocketTask) and file access for the G9SignalRSuperNetCore Lynx client.'
  s.homepage = 'https://github.com/ImanKari/G9SignalRSuperNetCore'
  s.license = { :type => 'MIT' }
  s.author = 'Iman Kari (G9TM)'
  s.platform = :ios, '15.0'
  s.source = { :path => '.' }
  s.source_files = 'src/**/*.{h,m}'
  s.frameworks = 'Foundation'
  s.requires_arc = true
  s.dependency 'Lynx', '4.0.3'
end
