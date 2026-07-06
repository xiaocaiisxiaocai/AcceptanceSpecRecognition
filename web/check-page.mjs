import puppeteer from 'puppeteer';

(async () => {
  const browser = await puppeteer.launch();
  const page = await browser.newPage();

  try {
    await page.goto('http://localhost:8851/#/smart-fill/fill', { waitUntil: 'networkidle2', timeout: 10000 });

    // 等待 MatchConfig 加载
    await page.waitForSelector('.match-config', { timeout: 5000 });

    // 获取 el-form-item 的实际样式
    const formItemStyle = await page.evaluate(() => {
      const elem = document.querySelector('.match-config .el-form-item');
      if (!elem) return 'NOT_FOUND';
      const style = window.getComputedStyle(elem);
      return {
        marginBottom: style.marginBottom,
        padding: style.padding,
        height: elem.offsetHeight
      };
    });

    console.log('Form Item 实际样式:', JSON.stringify(formItemStyle, null, 2));

    // 截图
    await page.screenshot({ path: 'smart-fill-check.png', fullPage: false });
    console.log('✅ 截图已保存');

  } catch (error) {
    console.error('❌ 检查失败:', error.message);
  } finally {
    await browser.close();
  }
})();
