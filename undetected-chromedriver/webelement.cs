using System.Collections.Generic;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;

public class WebElement : OpenQA.Selenium.WebElement
{
    public WebElement(WebDriver driver, string elementId) : base(driver, elementId) { }

    public void ClickSafe()
    {
        base.Click();
        //TODO  ((Chrome)WrappedDriver).Reconnect(0.1);
    }

    public List<WebElement> Children(string tag = null, bool recursive = false)
    {
        var script = "return [...arguments[0].children];";
        if (!string.IsNullOrEmpty(tag))
        {
            script += $"filter(node => node.tagName === '{tag.ToUpper()}');";
        }
        if (recursive)
        {
            return new List<WebElement>(_RecursiveChildren(this, tag));
        }
        return new List<WebElement>(WrappedDriver.ExecuteJavaScript<List<WebElement>>(script, this));
    }

    private IEnumerable<WebElement> _RecursiveChildren(WebElement element, string tag = null)
    {
        foreach (var child in element.Children())
        {
            if (tag == null || child.TagName.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }
            foreach (var grandChild in _RecursiveChildren(child, tag))
            {
                yield return grandChild;
            }
        }
    }
}
public class UCWebElement : WebElement
{
    private Dictionary<string, string> _attrs;

    public UCWebElement(WebDriver driver, string elementId) : base(driver, elementId) { }

    public Dictionary<string, string> Attrs
    {
        get
        {
            if (_attrs == null)
            {
                var script = @"
                var items = {}; 
                for (var index = 0; index < arguments[0].attributes.length; ++index) 
                {
                    items[arguments[0].attributes[index].name] = arguments[0].attributes[index].value; 
                }; 
                return items;";
                _attrs = JsonConvert.DeserializeObject<Dictionary<string, string>>(WrappedDriver.ExecuteJavaScript<string>(script, this));
            }
            return _attrs;
        }
    }

    public override string ToString()
    {
        var strAttrs = string.Join(" ", Attrs.Select(kv => $"{kv.Key}=\"{kv.Value}\""));
        return $"{GetType().Name} <{TagName} {(string.IsNullOrEmpty(strAttrs) ? "" : strAttrs)}>";
    }
}