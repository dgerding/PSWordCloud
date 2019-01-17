# PSWordCloud

Create pretty word clouds with PowerShell!

## Installation

```powershell
Install-Module PSWordCloud
```

### Linux & macOS

To get it working on Linux or macOS, you must install the `mono-libgdiplus` package on your machine for this to work.

For macOS, you can use Homebrew:

```sh
brew install mono-libgdiplus
```

For linux, use your package manager of choice to install it. For example, via Apt:

```sh
apt install libgdiplus
```

## Usage

```powershell
Get-ClipBoard | New-WordCloud -Path .\wordcloud.png -FontFamily Georgia

Get-Content .\words.txt | New-WordCloud -Path .\wordcloud2.png -ImageSize 1080p

Get-ClipBoard | New-WordCloud -Path .\wordcloud3.png -FontFamily Consolas -ImageSize 1000x1000
```

## Examples

Using the poem _Paterson_ by William Carlos Williams

### 4K 4096x2160 Resolution (Default)

![4K Color](./_images/Paterson_4K.png)

### 4K 4096x2160 Resolution (Monochrome)

![4K Monochrome](./_images/Paterson_4K_mono.png)

### 4K 4096x4096 Resolution (Default)

![4K Square, Color](./_images/Paterson_4K_sq.png)

### Mobile Format 640x1146 (Default)

![Mobile Color](./_images/Paterson_mobile.png)

### Mobile Format 640x1146 (Monochrome)

![Mobile Monochrome](./_images/Paterson_mobile_mono.png)
