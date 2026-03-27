class Anythink < Formula
  desc "CLI and MCP server for the Anythink backend-as-a-service platform"
  homepage "https://github.com/anythink-cloud/anythink-cli"
  version "${VERSION}"
  license "MIT"

  on_macos do
    if Hardware::CPU.arm?
      url "https://github.com/anythink-cloud/anythink-cli/releases/download/v${VERSION}/anythink-osx-arm64"
      sha256 "${SHA_OSX_ARM64}"

      resource "mcp" do
        url "https://github.com/anythink-cloud/anythink-cli/releases/download/v${VERSION}/anythink-mcp-osx-arm64"
        sha256 "${SHA_MCP_OSX_ARM64}"
      end
    else
      url "https://github.com/anythink-cloud/anythink-cli/releases/download/v${VERSION}/anythink-osx-x64"
      sha256 "${SHA_OSX_X64}"

      resource "mcp" do
        url "https://github.com/anythink-cloud/anythink-cli/releases/download/v${VERSION}/anythink-mcp-osx-x64"
        sha256 "${SHA_MCP_OSX_X64}"
      end
    end
  end

  on_linux do
    if Hardware::CPU.arm?
      url "https://github.com/anythink-cloud/anythink-cli/releases/download/v${VERSION}/anythink-linux-arm64"
      sha256 "${SHA_LINUX_ARM64}"

      resource "mcp" do
        url "https://github.com/anythink-cloud/anythink-cli/releases/download/v${VERSION}/anythink-mcp-linux-arm64"
        sha256 "${SHA_MCP_LINUX_ARM64}"
      end
    else
      url "https://github.com/anythink-cloud/anythink-cli/releases/download/v${VERSION}/anythink-linux-x64"
      sha256 "${SHA_LINUX_X64}"

      resource "mcp" do
        url "https://github.com/anythink-cloud/anythink-cli/releases/download/v${VERSION}/anythink-mcp-linux-x64"
        sha256 "${SHA_MCP_LINUX_X64}"
      end
    end
  end

  def install
    binary = Dir.glob("anythink-*").first || "anythink"
    mv binary, "anythink"
    chmod 0755, "anythink"
    bin.install "anythink"

    resource("mcp").stage do
      mcp_bin = Dir.glob("anythink-mcp-*").first || "anythink-mcp"
      mv mcp_bin, "anythink-mcp"
      chmod 0755, "anythink-mcp"
      bin.install "anythink-mcp"
    end
  end

  def caveats
    <<~EOS

       ░███                             ░██    ░██        ░██           ░██
      ░██░██                            ░██    ░██                      ░██
     ░██  ░██  ░████████  ░██    ░██ ░████████ ░████████  ░██░████████  ░██    ░██
    ░█████████ ░██    ░██ ░██    ░██    ░██    ░██    ░██ ░██░██    ░██ ░██   ░██
    ░██    ░██ ░██    ░██ ░██    ░██    ░██    ░██    ░██ ░██░██    ░██ ░███████
    ░██    ░██ ░██    ░██ ░██   ░███    ░██    ░██    ░██ ░██░██    ░██ ░██   ░██
    ░██    ░██ ░██    ░██  ░█████░██     ░████ ░██    ░██ ░██░██    ░██ ░██    ░██
                                 ░██
                           ░███████

    Whatever you're building, Anythink is the backend at your service.

    Get started:
      anythink login
      anythink --help

    MCP Server (for AI-powered development with Claude Code):
    Add the following to your .mcp.json:
      {
        "mcpServers": {
          "anythink": {
            "command": "anythink-mcp"
          }
        }
      }
    EOS
  end

  test do
    assert_match "anythink", shell_output("#{bin}/anythink --version")
  end
end
