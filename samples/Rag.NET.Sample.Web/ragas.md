# RAGAS
RAGAS (Retrieval Augmented Generation Assessment) is a framework for evaluating RAG pipelines — 
it scores both the retrieval and generation stages so you can tell where a pipeline is actually failing. 

The core metrics:

# 1] Retrieval-focused
* Context Precision — of the chunks retrieved, how many were actually relevant to the question? Penalizes noisy retrieval that pulls in irrelevant context.
* Context Recall — did the retrieved context contain everything needed to answer correctly? Measured against a reference/ground-truth answer. 
  Low recall means the retriever is missing important information.

# 2] Generation-focused

* Faithfulness — does the generated answer actually stick to what's in the retrieved context, or is the model hallucinating claims not supported by it?
  Computed by breaking the answer into individual statements and checking each against the context.

* Answer Relevancy — does the answer actually address the question asked, without being evasive or padded with irrelevant info? Typically computed by generating synthetic questions from the answer and comparing their similarity to the original question.

# 3] End-to-end / composite

* Answer Correctness — combines factual similarity to a ground-truth answer with semantic similarity.
* Answer Semantic Similarity — pure embedding-based similarity between generated and reference answers.


Why it's structured this way: the split between retrieval and generation metrics is the useful part — 
it lets you diagnose where a RAG system is breaking. High faithfulness + low context recall means the model is being honest but wasn't given enough to work with. Low faithfulness + high context precision means the retriever did its job but the generator is hallucinating anyway.

Most of these use an LLM-as-judge under the hood (RAGAS calls out to an LLM, often GPT-4-class, to score faithfulness/relevancy), so scores can shift a bit with the judge model and prompt version — worth pinning your evaluator model if you're tracking scores over time or across pipeline changes.