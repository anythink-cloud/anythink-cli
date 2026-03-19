class Anythink < Formula
  desc "CLI for the Anythink BaaS platform — manage projects, entities, workflows & data"
  homepage "https://anythink.cloud"
  version "1.0.0"
  license "MIT"

  on_macos do
    on_arm do
      url "https://github.com/Anythink-Ltd/anythink-cli/releases/download/v#{version}/anythink-osx-arm64"
      sha256 "REPLACE_WITH_SHA256_FOR_OSX_ARM64"
    end
    on_intel do
      url "https://github.com/Anythink-Ltd/anythink-cli/releases/download/v#{version}/anythink-osx-x64"
      sha256 "REPLACE_WITH_SHA256_FOR_OSX_X64"
    end
  end

  on_linux do
    on_arm do
      url "https://github.com/Anythink-Ltd/anythink-cli/releases/download/v#{version}/anythink-linux-arm64"
      sha256 "REPLACE_WITH_SHA256_FOR_LINUX_ARM64"
    end
    on_intel do
      url "https://github.com/Anythink-Ltd/anythink-cli/releases/download/v#{version}/anythink-linux-x64"
      sha256 "REPLACE_WITH_SHA256_FOR_LINUX_X64"
    end
  end

  def install
    bin.install stable.url.split("/").last => "anythink"
  end

  test do
    assert_match "anythink", shell_output("#{bin}/anythink --version")
  end
end
