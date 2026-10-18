namespace Locksy.Models;

/// <summary>Controls the paragraph direction of a task title, independently of the app language.</summary>
public enum TaskTextDirection
{
    /// <summary>Detect direction from the first strong letter in the title.</summary>
    Auto = 0,
    /// <summary>Force left-to-right paragraph direction.</summary>
    LeftToRight = 1,
    /// <summary>Force right-to-left paragraph direction.</summary>
    RightToLeft = 2
}
