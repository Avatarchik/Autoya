namespace AutoyaFramework.Information {
	/*
		tags with specific feature.
		no need to add other tags into this enum.

		the tag which is not contained in this enum will be resolved at runtime.
	 */
    public enum HTMLTag {
		html,
		head,
		body,

		title,
		
		h1,h2,h3,h4,h5,h6,
		pre,strong,code,em,

        a,
		p,
		
		// value itself tags.
		br,
		hr,
		img, 
		
		// table.
		table,
		thead,
		tbody,
		tr,
		th,
		td,

		// list.
        ul, 
        ol,
        li, 
		
		// system.
		_ROOT,
		_COMMENT,
		_NO_TAG_FOUND,
		_IGNORED_EXCLAMATION_TAG,
		_END,// use for enum count.
	}
}